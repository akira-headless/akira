using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Interfaces.Probing;

namespace Akira.Infrastructure.Probing;

public class ProbeService : IProbeService
{
    // A file ffprobe cannot read is tried again after as long as it has been failing, between these two
    private static readonly TimeSpan ShortestRetry = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromDays(1);

    private readonly AkiraDbContext _dbContext;
    private readonly IFfprobeRunner _ffprobe;
    private readonly IOptions<ProbingOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ProbeService> _logger;

    public ProbeService(
        AkiraDbContext dbContext,
        IFfprobeRunner ffprobe,
        IOptions<ProbingOptions> options,
        TimeProvider time,
        ILogger<ProbeService> logger)
    {
        _dbContext = dbContext;
        _ffprobe = ffprobe;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProbeRunResult> ProbePendingAsync(CancellationToken cancellationToken)
    {
        int probed = 0, missing = 0, failed = 0;
        var attempted = new HashSet<Guid>();
        var probedItems = new HashSet<Guid>();

        while (true)
        {
            var batch = await _dbContext.MediaFiles.AsNoTracking()
                .Where(file => file.ProbeStatus == ProbeStatus.Pending
                               || (file.ProbeStatus == ProbeStatus.Probed && file.ProbeVersion != FfprobeParser.Version))
                .OrderBy(file => file.CreatedAt)
                .Select(file => new PendingFile(file.Id, file.MediaItemId, file.Path, file.InstanceKey))
                .Take(_options.Value.BatchSize)
                .ToListAsync(cancellationToken);

            // A file the sync queues again while it is being probed waits for the next pass
            batch = batch.Where(file => attempted.Add(file.Id)).ToList();
            if (batch.Count == 0)
            {
                break;
            }

            var outcomes = await ProbeBatchAsync(batch, cancellationToken);
            var itemsByFile = batch.ToDictionary(file => file.Id, file => file.MediaItemId);
            var probedFiles = outcomes.Where(outcome => outcome.Status == ProbeStatus.Probed);
            probedItems.UnionWith(probedFiles.Select(outcome => itemsByFile[outcome.FileId]));
            probed += outcomes.Count(outcome => outcome.Status == ProbeStatus.Probed);
            missing += outcomes.Count(outcome => outcome.Status == ProbeStatus.Missing);
            failed += outcomes.Count(outcome => outcome.Status == ProbeStatus.Failed);
        }

        return new ProbeRunResult(probed, missing, failed, probedItems.ToList());
    }

    /// <inheritdoc />
    public async Task<ProbeRunResult> ProbeFilesAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken)
    {
        var files = await _dbContext.MediaFiles.AsNoTracking()
            .Where(file => fileIds.Contains(file.Id) && file.ProbeStatus == ProbeStatus.Pending)
            .Select(file => new PendingFile(file.Id, file.MediaItemId, file.Path, file.InstanceKey))
            .ToListAsync(cancellationToken);
        if (files.Count == 0)
        {
            return new ProbeRunResult(0, 0, 0, []);
        }

        var outcomes = await ProbeBatchAsync(files, cancellationToken);
        var itemsByFile = files.ToDictionary(file => file.Id, file => file.MediaItemId);
        return new ProbeRunResult(
            outcomes.Count(outcome => outcome.Status == ProbeStatus.Probed),
            outcomes.Count(outcome => outcome.Status == ProbeStatus.Missing),
            outcomes.Count(outcome => outcome.Status == ProbeStatus.Failed),
            outcomes.Where(outcome => outcome.Status == ProbeStatus.Probed).Select(outcome => itemsByFile[outcome.FileId]).Distinct().ToList());
    }

    /// <inheritdoc />
    public async Task<RescanResult> RescanAsync(CancellationToken cancellationToken)
    {
        int requeued = 0, added = 0, removed = 0;
        var changedItems = new HashSet<Guid>();
        // By path, so the videos of one folder follow each other and the folder is listed once for all of them
        var candidates = await _dbContext.MediaFiles.AsNoTracking()
            .Where(file => file.ProbeStatus != ProbeStatus.Pending)
            .OrderBy(file => file.Path)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);
        var folder = new FolderSubtitles();
        var now = _time.GetUtcNow();

        foreach (var chunk in candidates.Chunk(_options.Value.BatchSize))
        {
            var ids = chunk.ToList();
            var files = await _dbContext.MediaFiles
                .Include(file => file.Streams.Where(stream => stream.IsExternal))
                .Where(file => ids.Contains(file.Id))
                .OrderBy(file => file.Path)
                .ToListAsync(cancellationToken);

            foreach (var file in files)
            {
                var changed = HasChangedOnDisk(file);
                if (file.ProbeStatus == ProbeStatus.Failed && !changed && !IsRetryDue(file, now))
                {
                    continue;
                }

                if (file.ProbeStatus != ProbeStatus.Probed || changed)
                {
                    file.ProbeStatus = ProbeStatus.Pending;
                    requeued++;
                    continue;
                }

                var (subtitlesAdded, subtitlesRemoved) = await RescanSubtitlesAsync(file, folder, cancellationToken);
                added += subtitlesAdded;
                removed += subtitlesRemoved;
                if (subtitlesAdded + subtitlesRemoved > 0)
                {
                    changedItems.Add(file.MediaItemId);
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
        }

        return new RescanResult(requeued, added, removed, changedItems.ToList());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExternalTrack>> RescanSubtitlesAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await _dbContext.MediaFiles
            .Include(file => file.Streams.Where(stream => stream.IsExternal))
            .SingleOrDefaultAsync(file => file.Id == fileId, cancellationToken);
        if (file is null)
        {
            return [];
        }

        await RescanSubtitlesAsync(file, new FolderSubtitles(), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return file.Streams.Where(stream => stream.IsExternal && stream.ExternalPath is not null)
            .Select(stream => new ExternalTrack(stream.Id, stream.ExternalPath!))
            .ToList();
    }

    private async Task<ProbeOutcome[]> ProbeBatchAsync(List<PendingFile> batch, CancellationToken cancellationToken)
    {
        using var concurrency = new SemaphoreSlim(_options.Value.MaxConcurrency);
        var outcomes = await Task.WhenAll(batch.Select(async file =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                return await ProbeFileAsync(file, cancellationToken);
            }
            finally
            {
                concurrency.Release();
            }
        }));

        await ApplyAsync(outcomes, cancellationToken);
        return outcomes;
    }

    private async Task<ProbeOutcome> ProbeFileAsync(PendingFile file, CancellationToken cancellationToken)
    {
        var info = new FileInfo(file.Path);
        if (!info.Exists)
        {
            // The path goes to the log for the admin, not into the error every user can read
            _logger.LogWarning("File {Path} of instance {Instance} is not on disk", file.Path, file.InstanceKey);
            return ProbeOutcome.Problem(file.Id, ProbeStatus.Missing,
                $"Not on disk. Check the path mappings of instance {file.InstanceKey}.");
        }

        // Kept for a failure too: a file that failed and has not changed since waits longer before its next try
        var size = info.Length;
        var modified = ModifiedOnDisk(info);
        try
        {
            var output = await _ffprobe.ProbeAsync(file.Path, cancellationToken);
            var media = FfprobeParser.Parse(output, file.Id);
            var found = ExternalSubtitleScanner.Find(file.Path);
            var externals = await ProbeExternalSubtitlesAsync(file.Id, found, new HashSet<string>(), cancellationToken);
            media.Streams.AddRange(externals);
            return new ProbeOutcome(file.Id, ProbeStatus.Probed, null, media, size, modified);
        }
        catch (ProbeException exception) when (exception.Failure != ProbeFailure.NotInstalled)
        {
            return new ProbeOutcome(file.Id, ProbeStatus.Failed, exception.Message, null, size, modified);
        }
        catch (IOException exception)
        {
            return new ProbeOutcome(file.Id, ProbeStatus.Failed, $"Could not read {file.Path}: {exception.Message}", null, size, modified);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new ProbeOutcome(file.Id, ProbeStatus.Failed, $"Not allowed to read {file.Path}: {exception.Message}", null, size, modified);
        }
    }

    private async Task<List<MediaStream>> ProbeExternalSubtitlesAsync(
        Guid fileId,
        IReadOnlyList<ExternalSubtitleFile> found,
        IReadOnlySet<string> skipFileNames,
        CancellationToken cancellationToken)
    {
        var streams = new List<MediaStream>();
        for (var index = 0; index < found.Count; index++)
        {
            var subtitle = found[index];
            if (skipFileNames.Contains(subtitle.FileName))
            {
                continue;
            }

            try
            {
                var output = await _ffprobe.ProbeSubtitleAsync(subtitle.FullPath, cancellationToken);
                var stream = FfprobeParser.ParseExternalSubtitle(output, fileId, subtitle.FileName, subtitle.FullPath,
                    subtitle.Name, FfprobeParser.ExternalSortOrderStart + index);
                if (stream is not null)
                {
                    streams.Add(stream);
                }
            }
            catch (ProbeException exception) when (exception.Failure != ProbeFailure.NotInstalled)
            {
                // One unreadable subtitle file must not fail the whole video
                _logger.LogWarning("Skipped subtitle {Path}: {Reason}", subtitle.FullPath, exception.Message);
            }
        }

        return streams;
    }

    private async Task<(int Added, int Removed)> RescanSubtitlesAsync(MediaFile file, FolderSubtitles folder, CancellationToken cancellationToken)
    {
        if (folder.Of(file.Path, _logger) is not { } listing)
        {
            // The subtitles it had stay, a directory that cannot be listed says nothing about them
            return (0, 0);
        }

        var found = ExternalSubtitleScanner.Find(file.Path, listing);

        var foundNames = found.Select(subtitle => subtitle.FileName).ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var stream in file.Streams.Where(stream => stream.IsExternal).ToList())
        {
            if (!foundNames.Contains(Path.GetFileName(stream.ExternalPath ?? string.Empty)))
            {
                file.Streams.Remove(stream);
                _dbContext.MediaStreams.Remove(stream);
                removed++;
            }
        }

        var known = file.Streams.Select(stream => Path.GetFileName(stream.ExternalPath ?? string.Empty)).ToHashSet(StringComparer.Ordinal);
        var added = await ProbeExternalSubtitlesAsync(file.Id, found, known, cancellationToken);
        foreach (var stream in added)
        {
            file.Streams.Add(stream);
            _dbContext.MediaStreams.Add(stream);
        }

        return (added.Count, removed);
    }

    private async Task ApplyAsync(ProbeOutcome[] outcomes, CancellationToken cancellationToken)
    {
        var ids = outcomes.Select(outcome => outcome.FileId).ToList();
        var files = await _dbContext.MediaFiles
            .Include(file => file.Streams)
            .Include(file => file.Chapters)
            .Where(file => ids.Contains(file.Id))
            .ToDictionaryAsync(file => file.Id, cancellationToken);

        foreach (var outcome in outcomes)
        {
            // The sync may have replaced or removed the file while it was being probed
            if (!files.TryGetValue(outcome.FileId, out var file))
            {
                continue;
            }

            file.ProbeStatus = outcome.Status;
            file.ProbeError = outcome.Error;
            file.ProbedAt = _time.GetUtcNow();
            file.ProbeVersion = FfprobeParser.Version;
            if (outcome.SizeOnDisk is not null)
            {
                file.ProbedSizeBytes = outcome.SizeOnDisk;
                file.ProbedModifiedAt = outcome.ModifiedOnDisk;
            }

            if (outcome.Media is null)
            {
                continue;
            }

            file.DurationMs = outcome.Media.DurationMs;
            ReplaceStreams(file, outcome.Media.Streams);

            _dbContext.MediaChapters.RemoveRange(file.Chapters);
            file.Chapters.Clear();
            foreach (var chapter in outcome.Media.Chapters)
            {
                file.Chapters.Add(chapter);
                _dbContext.MediaChapters.Add(chapter);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    // The ids are stable, so a probe of an unchanged file updates the same rows instead of
    // deleting and inserting tracks with the same key
    private void ReplaceStreams(MediaFile file, List<MediaStream> streams)
    {
        var existing = file.Streams.ToDictionary(stream => stream.Id);
        foreach (var stream in streams)
        {
            if (existing.Remove(stream.Id, out var current))
            {
                _dbContext.Entry(current).CurrentValues.SetValues(stream);
                continue;
            }

            file.Streams.Add(stream);
            _dbContext.MediaStreams.Add(stream);
        }

        foreach (var stale in existing.Values)
        {
            file.Streams.Remove(stale);
            _dbContext.MediaStreams.Remove(stale);
        }
    }

    private static bool HasChangedOnDisk(MediaFile file)
    {
        var info = new FileInfo(file.Path);
        return !info.Exists || info.Length != file.ProbedSizeBytes || ModifiedOnDisk(info) != file.ProbedModifiedAt;
    }

    // A broken file costs ffprobe up to its timeout on every try. Each wait is as long as the file has been failing, so
    // one that was only half copied is read soon and one that is broken for good once a day. A newer parser tries at once.
    private static bool IsRetryDue(MediaFile file, DateTimeOffset now)
    {
        if (file.ProbedAt is not { } last || file.ProbeVersion != FfprobeParser.Version)
        {
            return true;
        }

        var failing = last - file.CreatedAt;
        var wait = failing < ShortestRetry ? ShortestRetry : failing > LongestRetry ? LongestRetry : failing;
        return now - last >= wait;
    }

    // Whole seconds: MariaDB stores microseconds and file times have 100 ns ticks, so the exact
    // value would never compare equal after a round trip, and NFS often only keeps seconds anyway
    private static DateTimeOffset ModifiedOnDisk(FileInfo info)
    {
        var utc = info.LastWriteTimeUtc;
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    /// <summary>
    /// Represents a file waiting to be probed, either new or probed by an older parser version.
    /// </summary>
    private sealed record PendingFile(Guid Id, Guid MediaItemId, string Path, string InstanceKey);

    // The subtitle files of the folder asked for last. The rescan goes through the library by path, so a flat folder of
    // a thousand videos is listed once, not once for each video.
    private sealed class FolderSubtitles
    {
        private string? _directory;
        private IReadOnlyList<string>? _subtitles;

        public IReadOnlyList<string>? Of(string videoPath, ILogger logger)
        {
            var directory = Path.GetDirectoryName(videoPath);
            if (string.IsNullOrEmpty(directory))
            {
                return [];
            }

            if (directory == _directory)
            {
                return _subtitles;
            }

            _directory = directory;
            try
            {
                _subtitles = ExternalSubtitleScanner.List(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Could not list the subtitles in {Directory}: {Reason}", directory, exception.Message);
                _subtitles = null;
            }

            return _subtitles;
        }
    }

    /// <summary>
    /// Represents what probing one file came to. Media is only set when it was probed, SizeOnDisk and ModifiedOnDisk
    /// when the file was there.
    /// </summary>
    private sealed record ProbeOutcome(
        Guid FileId,
        ProbeStatus Status,
        string? Error,
        ProbedMedia? Media,
        long? SizeOnDisk,
        DateTimeOffset? ModifiedOnDisk)
    {
        public static ProbeOutcome Problem(Guid fileId, ProbeStatus status, string error) =>
            new(fileId, status, error, null, null, null);
    }
}
