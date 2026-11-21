using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Akira.Core.Playback;
using Akira.Infrastructure.Processes;

namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Runs one ffmpeg per session that writes HLS segments to a folder. A player asking for a segment far from
/// where ffmpeg is, a seek, restarts it there; the timestamps keep every segment where the playlist says it is.
/// </summary>
public sealed class HlsTranscoder : IHlsTranscoder, IDisposable
{
    // Within this many segments past what is done, waiting is quicker than starting over
    private const int LookAhead = 3;
    private const int MaxErrorChars = 2000;

    // Three minutes of reports at one every two seconds
    private const int HistoryLength = 90;

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    private readonly IOptionsMonitor<TranscodingOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<HlsTranscoder> _logger;
    private readonly ConcurrentDictionary<Guid, Slot> _slots = new();

    // Set when a GPU run failed: until a restart, everything is encoded with libx264
    private volatile bool _softwareOnly;

    // Places taken by HLS and progressive runs alike, each given back when its ffmpeg is gone
    private int _reserved;

    public HlsTranscoder(
        IOptionsMonitor<TranscodingOptions> options,
        TimeProvider time,
        ILogger<HlsTranscoder> logger)
    {
        _options = options;
        _time = time;
        _logger = logger;
    }

    public int Running => Volatile.Read(ref _reserved);

    /// <inheritdoc />
    public bool TryReserve()
    {
        var max = _options.CurrentValue.MaxConcurrent;
        while (true)
        {
            var taken = Volatile.Read(ref _reserved);
            if (taken >= max)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _reserved, taken + 1, taken) == taken)
            {
                return true;
            }
        }
    }

    /// <inheritdoc />
    public void Release() => Interlocked.Decrement(ref _reserved);

    public bool SoftwareOnly => _softwareOnly;

    /// <inheritdoc />
    public TranscodeStatus? Status(Guid sessionId)
    {
        if (!_slots.TryGetValue(sessionId, out var slot) || slot.Current is not { } run)
        {
            return null;
        }

        lock (slot.History)
        {
            return new TranscodeStatus(run.Encoder, !run.HasExited, EncodedTo(run), slot.History.ToList());
        }
    }

    /// <inheritdoc />
    public async Task<string> GetSegmentAsync(TranscodeJob job, int index, CancellationToken cancellationToken)
    {
        var settings = _options.CurrentValue;
        var segments = job.SegmentsFor(settings);
        if (index < 0 || index >= segments.Count)
        {
            throw new TranscodeException(TranscodeFailure.OutOfRange, $"Segment {index} is not part of this stream.");
        }

        // Created as used, so the idle check never sees a new slot without a time
        var slot = _slots.GetOrAdd(job.SessionId, _ => NewSlot(settings));
        slot.LastAccess = _time.GetUtcNow();

        Run run;
        string path;
        await slot.Gate.WaitAsync(cancellationToken);
        try
        {
            if (slot.Current is { } previous && previous.Key != job.Key)
            {
                await KillAsync(previous);
                slot.Current = null;
                Retire(slot);
            }

            slot.Requested = index;
            path = SegmentPath(slot.Directory, index);
            // Read before the file is looked for: a segment at or before it that is not there was removed, or never made
            var reach = slot.Current?.Reach() ?? -1;
            RemoveBehind(slot, segments, index, reach, settings.KeepBehind);
            if (File.Exists(path))
            {
                await ResumeAsync(slot, job, index, reach, segments.Count, settings);
                return path;
            }

            if (slot.Current is not { } current || current.HasExited || current.Killed || index <= reach || index > reach + LookAhead)
            {
                if (slot.Current is { } old)
                {
                    await KillAsync(old);
                }

                slot.Current = Start(slot, job, index, settings);
            }

            run = slot.Current;
            // Counted inside the gate, so a run somebody waits on is never stopped for being ahead
            Interlocked.Increment(ref slot.Waiting);
        }
        finally
        {
            slot.Gate.Release();
        }

        try
        {
            return await WaitOrFallBackAsync(slot, run, job, index, path, settings, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref slot.Waiting);
        }
    }

    private async Task<string> WaitOrFallBackAsync(Slot slot, Run run, TranscodeJob job, int index, string path, TranscodingOptions settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await WaitForAsync(slot, run, path, settings.SegmentTimeout, cancellationToken);
        }
        // ffmpeg failing on its own may be the GPU, but also a full disk or a subtitle it cannot read. So the same
        // segment is made on the CPU, and only when that works was the GPU the problem.
        catch (TranscodeException exception) when (exception.Failure == TranscodeFailure.Failed && run.Hardware != HardwareAcceleration.None
                                                   && !slot.Stopped)
        {
            await slot.Gate.WaitAsync(cancellationToken);
            try
            {
                if (slot.Current is not { } current || current == run || current.HasExited)
                {
                    if (slot.Current is { } failed)
                    {
                        await KillAsync(failed);
                    }

                    slot.Current = Start(slot, job, index, settings, software: true);
                }

                run = slot.Current;
            }
            finally
            {
                slot.Gate.Release();
            }

            var segment = await WaitForAsync(slot, run, path, settings.SegmentTimeout, cancellationToken);
            if (run.Hardware == HardwareAcceleration.None)
            {
                FallBackToSoftware(exception.Message);
            }

            return segment;
        }
    }

    /// <inheritdoc />
    public void FallBackToSoftware(string reason)
    {
        if (_softwareOnly)
        {
            return;
        }

        _softwareOnly = true;
        _logger.LogError("Encoding with {Hardware} failed, libx264 encodes until the server restarts: {Reason}",
            _options.CurrentValue.HardwareAcceleration, reason);
    }

    /// <inheritdoc />
    public void Stop(Guid sessionId)
    {
        if (!_slots.TryRemove(sessionId, out var slot))
        {
            return;
        }

        // Nothing starts on it any more; a request inside the gate finishes first, then its run is ended with the rest
        slot.Stopped = true;
        _ = Task.Run(async () =>
        {
            await slot.Gate.WaitAsync();
            try
            {
                if (slot.Current is { } run)
                {
                    await KillAsync(run);
                }

                if (slot.HoldsPlace)
                {
                    slot.HoldsPlace = false;
                    Release();
                }
            }
            finally
            {
                slot.Gate.Release();
            }

            foreach (var directory in slot.Retired)
            {
                TryDeleteDirectory(directory, log: true);
            }

            TryDeleteDirectory(slot.Directory, log: true);
        });
    }

    /// <inheritdoc />
    public void StopIdle()
    {
        var cutoff = _time.GetUtcNow() - _options.CurrentValue.IdleTimeout;
        foreach (var (sessionId, slot) in _slots)
        {
            if (slot.LastAccess < cutoff)
            {
                _logger.LogInformation("Stopping the transcode of session {SessionId}, no segment was asked for since {LastAccess}", sessionId,
                    slot.LastAccess);
                Stop(sessionId);
            }
        }
    }

    public void Dispose()
    {
        // Shutting down: every ffmpeg goes at once, nothing is waited for
        foreach (var (_, slot) in _slots)
        {
            slot.Stopped = true;
            if (slot.Current is { } run)
            {
                run.Killed = true;
                ProcessRunner.Kill(run.Process);
            }
        }

        _slots.Clear();
    }

    // A folder per slot, not per session: after an idle stop the player can resume while the old slot's folder is still being removed
    private Slot NewSlot(TranscodingOptions settings) => new(settings.ResolveTempPath()) { LastAccess = _time.GetUtcNow() };

    // Another track gets another folder. Emptying the one there is leaves a segment a player still reads on Windows,
    // and that segment would then be served with the old track.
    private void Retire(Slot slot)
    {
        slot.Retired.Add(slot.Directory);
        slot.Directory = Slot.NewDirectory(slot.Root);
        slot.Floor = int.MaxValue;
        slot.Retired.RemoveAll(directory => TryDeleteDirectory(directory, log: false));
    }

    // Segments well behind the player go, or a long film fills the disk. One the player seeks back to is made again.
    private static void RemoveBehind(Slot slot, HlsSegments segments, int index, int reach, TimeSpan keep)
    {
        var from = segments.Start(index) - keep.TotalSeconds;
        var first = index;
        while (first > 0 && segments.End(first - 1) > from)
        {
            first--;
        }

        // Never past what the run has written, it may still be on its way to the segment asked for
        first = Math.Min(first, reach + 1);
        for (var behind = slot.Floor; behind < first; behind++)
        {
            try
            {
                File.Delete(SegmentPath(slot.Directory, behind));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Still being read on Windows; it goes with the folder
            }
        }

        if (slot.Floor < first)
        {
            slot.Floor = first;
        }
    }

    // A run stopped for being far ahead starts again a few segments before the player reaches its last one
    private async Task ResumeAsync(Slot slot, TranscodeJob job, int index, int reach, int count, TranscodingOptions settings)
    {
        if (slot.Current is not { Throttled: true } paused || reach - index > LookAhead || reach + 1 >= count || slot.Stopped)
        {
            return;
        }

        await KillAsync(paused);
        try
        {
            slot.Current = Start(slot, job, reach + 1, settings);
        }
        catch (TranscodeException exception)
        {
            // This segment is there; the request for the first one that is missing says what is wrong
            _logger.LogWarning("Could not go on with the transcode of session {SessionId}: {Reason}", job.SessionId, exception.Message);
        }
    }

    private Run Start(Slot slot, TranscodeJob job, int index, TranscodingOptions settings, bool software = false)
    {
        if (slot.Stopped)
        {
            throw new TranscodeException(TranscodeFailure.Stopped, "The transcode of this session was stopped.");
        }

        // A run stopped for being ahead left its place to the one that follows it
        if (!slot.HoldsPlace && !TryReserve())
        {
            throw new TranscodeException(TranscodeFailure.Busy,
                $"The server is already transcoding {Running} streams, the most {TranscodingOptions.SectionName}:" +
                $"{nameof(TranscodingOptions.MaxConcurrent)} allows.");
        }

        slot.HoldsPlace = false;
        try
        {
            Directory.CreateDirectory(slot.Directory);
            var hardware = software || _softwareOnly ? HardwareAcceleration.None : settings.HardwareAcceleration;
            var name = Guid.NewGuid().ToString("N")[..8];
            Process process;
            try
            {
                process = ProcessRunner.Start(settings.FfmpegPath, FfmpegHlsArguments.Build(job, settings, index, slot.Directory, hardware, name));
            }
            catch (ProcessRunException exception)
            {
                throw new TranscodeException(TranscodeFailure.NotInstalled, settings.FfmpegMissing(), exception);
            }

            var encoder = job.Decision.CopyVideo ? "copy" : Encoder(hardware);
            _logger.LogInformation("Transcoding session {SessionId} from segment {Index} ({Decision}, {Encoder})", job.SessionId, index,
                job.Decision.ToToken(), encoder);
            var runHardware = job.Decision.CopyVideo ? HardwareAcceleration.None : hardware;
            var run = new Run(process, job.Key, index, job.SegmentsFor(settings), job.CutsOnKeyframes ? name : null, runHardware, encoder, slot,
                settings.MaxAhead, _time);
            slot.Floor = Math.Min(slot.Floor, index);
            // The place is given back when ffmpeg is gone, whichever way it went, unless the session goes on later
            run.Released = run.Drained.ContinueWith(_ =>
            {
                if (run.Throttled && !slot.Stopped)
                {
                    slot.HoldsPlace = true;
                }
                else
                {
                    Release();
                }
            }, TaskScheduler.Default);
            return run;
        }
        catch
        {
            Release();
            throw;
        }
    }

    private static async Task<string> WaitForAsync(Slot slot, Run run, string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            // A seek of the same player replaced the run this request was waiting on
            if (run.HasExited && slot.Current is { } newer && newer != run)
            {
                run = newer;
                continue;
            }

            if (run.HasExited)
            {
                // The segment may have landed between the two checks
                await run.Finished;
                if (File.Exists(path))
                {
                    break;
                }

                if (run.Killed || slot.Stopped)
                {
                    throw new TranscodeException(TranscodeFailure.Stopped, "The transcode of this session was stopped.");
                }

                var error = run.Error;
                var detail = string.IsNullOrWhiteSpace(error) ? $"exit code {run.ExitCode}" : error;
                throw new TranscodeException(TranscodeFailure.Failed, "ffmpeg stopped before the segment was ready: " + detail);
            }

            if (deadline.Elapsed > timeout)
            {
                throw new TranscodeException(TranscodeFailure.TooSlow,
                    $"The segment was not ready within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds, " +
                    "the server transcodes slower than the video plays.");
            }

            await Task.Delay(Poll, cancellationToken);
        }

        return path;
    }

    private static string Encoder(HardwareAcceleration hardware) => hardware switch
    {
        HardwareAcceleration.Nvenc => "h264_nvenc",
        HardwareAcceleration.Qsv => "h264_qsv",
        HardwareAcceleration.Vaapi => "h264_vaapi",
        HardwareAcceleration.Amf => "h264_amf",
        _ => "libx264"
    };

    // From the segments on disk, as ffmpeg's own output time does not say where in the file a run is
    private static long? EncodedTo(Run run)
    {
        var reach = run.Reach();
        return reach < run.Start ? null : (long)Math.Round(run.Segments.End(reach) * 1000);
    }

    private static string SegmentPath(string directory, int index) =>
        Path.Combine(directory, index.ToString(CultureInfo.InvariantCulture) + ".ts");

    // Waits without holding a thread, so a seek or a track change does not block one for the seconds ffmpeg takes to go
    private static async Task KillAsync(Run run)
    {
        run.Killed = true;
        ProcessRunner.Kill(run.Process);
        try
        {
            // The place given back, not only the output read: a seek at the limit starts the next run in it right after.
            // Its segments renamed too, so none lands in a folder that is emptied after this.
            await run.Released.WaitAsync(TimeSpan.FromSeconds(5));
            await run.Finished.WaitAsync(TimeSpan.FromSeconds(5));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await run.Process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or InvalidOperationException)
        {
        }

        run.Process.Dispose();
    }

    private bool TryDeleteDirectory(string directory, bool log)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (log)
            {
                _logger.LogWarning("Could not remove the transcode folder {Directory}: {Message}", directory, exception.Message);
            }

            return false;
        }
    }

    private sealed class Slot
    {
        public string Root { get; }
        public string Directory { get; set; }

        // Folders of earlier tracks that could not be removed yet
        public List<string> Retired { get; } = [];
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Run? Current { get; set; }
        public DateTimeOffset LastAccess { get; set; }
        public volatile bool Stopped;

        // The segment asked for last, which is about where the player is
        public volatile int Requested;

        // Requests waiting for a segment that is being made
        public int Waiting;

        // The lowest segment that may be on disk
        public int Floor { get; set; } = int.MaxValue;

        // One of the Transcoding:MaxConcurrent places, kept while no ffmpeg runs because it was stopped for being ahead
        public volatile bool HoldsPlace;

        // Kept across the runs of a session, so a seek does not empty the graph
        public Queue<TranscodeSample> History { get; } = new();

        public Slot(string root)
        {
            Root = root;
            Directory = NewDirectory(root);
        }

        public static string NewDirectory(string root) => Path.Combine(root, Guid.CreateVersion7().ToString("N"));

        public void Add(TranscodeSample sample)
        {
            lock (History)
            {
                History.Enqueue(sample);
                while (History.Count > HistoryLength)
                {
                    History.Dequeue();
                }
            }
        }
    }

    private sealed class Run
    {
        private readonly StringBuilder _error = new();

        private readonly Slot _slot;
        private readonly string _directory;
        private readonly TimeSpan _maxAhead;
        private readonly TimeProvider _time;
        private readonly FfmpegProgress _progress = new();
        private readonly Lock _reachLock = new();

        // The name a run cut on keyframes writes its segments under until they are complete, null for one that writes .ts itself
        private readonly string? _parts;
        private int _reach;

        public Run(Process process, string key, int start, HlsSegments segments, string? parts, HardwareAcceleration hardware, string encoder,
            Slot slot, TimeSpan maxAhead, TimeProvider time)
        {
            Process = process;
            Key = key;
            Start = start;
            Segments = segments;
            _parts = parts;
            Hardware = hardware;
            Encoder = encoder;
            _slot = slot;
            _directory = slot.Directory;
            _maxAhead = maxAhead;
            _time = time;
            _reach = start - 1;
            // Both pipes are read to the end, a full pipe would make ffmpeg wait forever
            Drained = Task.WhenAll(Drain(process.StandardError, keep: true), Drain(process.StandardOutput, keep: false));
            Finished = FollowAsync();
        }

        public string Encoder { get; }

        public Process Process { get; }
        public string Key { get; }
        public int Start { get; }
        public HlsSegments Segments { get; }
        public Task Drained { get; }

        // Its output read and, for a run cut on keyframes, its finished segments renamed
        public Task Finished { get; }
        public HardwareAcceleration Hardware { get; }
        public Task Released { get; set; } = Task.CompletedTask;

        // Ended by the server (a seek, a stop, too far ahead), not failed on its own
        public volatile bool Killed;

        // Ended for being far ahead of the player; a request near its last segment starts it again
        public volatile bool Throttled;

        // The last segment on disk without a gap from where this run started. Kept, so a request does not walk the folder
        // from the start of the run, and segments removed behind the player do not end the count.
        public int Reach()
        {
            lock (_reachLock)
            {
                while (File.Exists(SegmentPath(_directory, _reach + 1)))
                {
                    _reach++;
                }

                return _reach;
            }
        }

        public bool HasExited
        {
            get
            {
                try
                {
                    return Process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public int? ExitCode
        {
            get
            {
                try
                {
                    return Process.ExitCode;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public string Error
        {
            get
            {
                lock (_error)
                {
                    return _error.ToString().Trim();
                }
            }
        }

        // The segment muxer closes a segment before it opens the next, so one is complete once the next exists, and the
        // last once ffmpeg ended well. A run that was stopped leaves its last one half written, which goes.
        private async Task FollowAsync()
        {
            var next = Start;
            while (!HasExited && !Killed)
            {
                if (_parts is not null)
                {
                    next = Rename(next, last: false);
                }

                StopWhenFarAhead();
                await Task.Delay(Poll);
            }

            await Drained;
            if (_parts is null)
            {
                return;
            }

            if (!Killed)
            {
                Rename(next, last: ExitCode == 0);
            }

            try
            {
                foreach (var part in Directory.EnumerateFiles(_directory, $"*.{_parts}{FfmpegHlsArguments.PartExtension}"))
                {
                    TryDelete(part);
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        // ffmpeg copies a film in minutes: without a limit a remux writes the whole file to the temp folder while the
        // player is in its first scene. The gate is only tried, whoever holds it may be waiting for this run to end.
        private void StopWhenFarAhead()
        {
            if (!IsFarAhead() || Volatile.Read(ref _slot.Waiting) > 0 || !_slot.Gate.Wait(0))
            {
                return;
            }

            try
            {
                if (!Killed && _slot.Current == this && Volatile.Read(ref _slot.Waiting) == 0 && IsFarAhead())
                {
                    Throttled = true;
                    Killed = true;
                    ProcessRunner.Kill(Process);
                }
            }
            finally
            {
                _slot.Gate.Release();
            }
        }

        private bool IsFarAhead()
        {
            var reach = Reach();
            var requested = Math.Min(_slot.Requested, Segments.Count - 1);
            // More than the few segments a request near the end of this run starts it again for, or it would stop at once
            return reach - requested > LookAhead + 1 && Segments.End(reach) - Segments.End(requested) > _maxAhead.TotalSeconds;
        }

        private int Rename(int index, bool last)
        {
            while (File.Exists(PartPath(index)) && (last || File.Exists(PartPath(index + 1))))
            {
                try
                {
                    File.Move(PartPath(index), SegmentPath(_directory, index), overwrite: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Windows keeps a file that a player still reads; the copy it has from an earlier run is the same segment
                    TryDelete(PartPath(index));
                }

                index++;
            }

            return index;
        }

        private string PartPath(int index) =>
            Path.Combine(_directory, $"{index.ToString(CultureInfo.InvariantCulture)}.{_parts}{FfmpegHlsArguments.PartExtension}");

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        private async Task Drain(StreamReader reader, bool keep)
        {
            try
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (!keep)
                    {
                        if (_progress.Read(line, _time.GetUtcNow()) is { } sample)
                        {
                            _slot.Add(sample);
                        }

                        continue;
                    }

                    lock (_error)
                    {
                        _error.AppendLine(line);
                        if (_error.Length > MaxErrorChars)
                        {
                            _error.Remove(0, _error.Length - MaxErrorChars);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }
}
