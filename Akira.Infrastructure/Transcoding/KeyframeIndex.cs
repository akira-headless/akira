using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Akira.Infrastructure.Interfaces.Probing;

namespace Akira.Infrastructure.Transcoding;

public sealed class KeyframeIndex : IKeyframeIndex
{
    // Every segment request asks, so the files of the streams that play now stay known
    private const int Capacity = 64;

    private readonly IFfprobeRunner _ffprobe;
    private readonly ILogger<KeyframeIndex> _logger;
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public KeyframeIndex(
        IFfprobeRunner ffprobe,
        ILogger<KeyframeIndex> logger)
    {
        _ffprobe = ffprobe;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<double>?> GetAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        if (_entries.TryGetValue(path, out var known) && known.Length == file.Length && known.Modified == file.LastWriteTimeUtc)
        {
            known.LastUsed = Environment.TickCount64;
            return known.Keyframes;
        }

        var keyframes = await ReadAsync(path, cancellationToken);
        if (_entries.Count >= Capacity)
        {
            foreach (var oldest in _entries.OrderBy(entry => entry.Value.LastUsed).Take(_entries.Count - Capacity + 1))
            {
                _entries.TryRemove(oldest.Key, out _);
            }
        }

        _entries[path] = new Entry(file.Length, file.LastWriteTimeUtc, keyframes) { LastUsed = Environment.TickCount64 };
        return keyframes;
    }

    private async Task<IReadOnlyList<double>?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // Matroska's Cues, or the sample table of MP4 and QuickTime
            if ((await MatroskaCues.ReadAsync(path, cancellationToken) ?? await Mp4SampleTable.ReadAsync(path, cancellationToken)) is not { } cues)
            {
                return null;
            }

            // -start_at_zero moves the file's first timestamp to 0, which is not always where the cues count from: an
            // audio track that starts early makes it negative
            var probe = await _ffprobe.ProbeAsync(path, cancellationToken);
            var start = double.TryParse(probe.Format?.StartTime, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
            return cues.Select(time => Math.Max(0, time - start)).ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A broken index costs the exact cuts, never the playback
            _logger.LogWarning("Could not read the keyframes of {Path}, its copied video is cut every segment length: {Message}", path,
                exception.Message);
            return null;
        }
    }

    /// <summary>
    /// Represents the keyframes read from one version of a file, null when it had none to read.
    /// </summary>
    private sealed record Entry(long Length, DateTime Modified, IReadOnlyList<double>? Keyframes)
    {
        public long LastUsed { get; set; }
    }
}
