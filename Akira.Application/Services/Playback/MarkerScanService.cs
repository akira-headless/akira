using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Markers;

namespace Akira.Server.Services.Playback;

/// <summary>
/// Finds intros and end credits by comparing the audio of the episodes of a season: what they share near the start is
/// the intro, near the end the credits. A start made of clips of the episodes before it is a recap.
/// </summary>
public sealed class MarkerScanService : IMarkerScanService
{
    // Raise when the matching changes, so every season is compared again
    public const int Version = 2;

    // Never more of the start than this share of the episode, nor of the end for the credits
    private const double MaxIntroShare = 0.4;
    private const double MaxCreditsShare = 0.3;

    // A recap mostly shows the episode before, now and then one further back
    private const int RecapSources = 3;

    private static readonly string CacheSuffix = $".v{AudioFingerprinter.Version}.fp";

    private readonly AkiraDbContext _dbContext;
    private readonly IAudioFingerprinter _fingerprinter;
    private readonly IOptions<MarkerOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<MarkerScanService> _logger;

    public MarkerScanService(
        AkiraDbContext dbContext,
        IAudioFingerprinter fingerprinter,
        IOptions<MarkerOptions> options,
        TimeProvider time,
        ILogger<MarkerScanService> logger)
    {
        _dbContext = dbContext;
        _fingerprinter = fingerprinter;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Represents the file of an episode in a season scan. AudioIndex is the index hint of its first audio track.
    /// </summary>
    private sealed record EpisodeFile(Guid FileId, int Episode, string Path, long DurationMs, int AudioIndex);

    /// <inheritdoc />
    public async Task<bool> ScanNextSeasonAsync(CancellationToken cancellationToken)
    {
        var seasonId = await _dbContext.MediaFiles.AsNoTracking()
            .Where(f => f.MediaItem.Type == MediaItemType.Episode && f.MediaItem.ParentId != null && f.ProbeStatus == ProbeStatus.Probed
                        && f.DurationMs != null && f.Streams.Any(s => s.Kind == StreamKind.Audio && !s.IsExternal))
            .Where(f => !_dbContext.MarkerScans.Any(scan => scan.MediaFileId == f.Id && scan.Version == Version))
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.MediaItem.ParentId)
            .FirstOrDefaultAsync(cancellationToken);
        if (seasonId is null)
        {
            return false;
        }

        var rows = await _dbContext.MediaFiles.AsNoTracking()
            .Where(f => f.MediaItem.ParentId == seasonId && f.MediaItem.Type == MediaItemType.Episode && f.ProbeStatus == ProbeStatus.Probed
                        && f.DurationMs != null)
            .Select(f => new
            {
                f.Id,
                Episode = f.MediaItem.EpisodeNumber ?? 0,
                f.Path,
                Duration = f.DurationMs!.Value,
                Audio = f.Streams.Where(s => s.Kind == StreamKind.Audio && !s.IsExternal && s.IndexHint != null)
                    .OrderBy(s => s.SortOrder).Select(s => s.IndexHint).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
        var files = rows
            .Where(f => f.Audio is not null)
            .OrderBy(f => f.Episode)
            .Select(f => new EpisodeFile(f.Id, f.Episode, f.Path, f.Duration, f.Audio!.Value))
            .ToList();

        var settings = _options.Value;
        var errors = new Dictionary<Guid, string>();
        var starts = new Dictionary<Guid, uint[]>();
        var intros = new List<(Guid, int, uint[])>();
        var credits = new List<(Guid, int, uint[])>();
        foreach (var file in files)
        {
            try
            {
                var introPoints = await FingerprintAsync(file, intro: true, cancellationToken);
                starts[file.FileId] = introPoints;
                intros.Add((file.FileId, file.Episode, introPoints));
                var creditPoints = await FingerprintAsync(file, intro: false, cancellationToken);
                credits.Add((file.FileId, file.Episode, creditPoints));
            }
            catch (FingerprintException exception) when (exception.Failure == FingerprintFailure.Failed)
            {
                errors[file.FileId] = exception.Message;
            }
        }

        var foundIntros = AudioMatch.InSeason(intros, new AudioMatchOptions(settings.MinLength, settings.MaxIntro));
        // Credits run as long as they run; the window bounds them
        var foundCredits = AudioMatch.InSeason(credits, new AudioMatchOptions(settings.MinLength, settings.CreditsWindow));

        var foundRecaps = settings.Recaps
            ? await FindRecapsAsync(files, starts, foundIntros, errors, cancellationToken)
            : new Dictionary<Guid, AudioSpan>();

        var ids = files.Select(f => f.FileId).ToList();
        var scans = await _dbContext.MarkerScans
            .Where(scan => ids.Contains(scan.MediaFileId))
            .ToDictionaryAsync(scan => scan.MediaFileId, cancellationToken);
        foreach (var file in files)
        {
            if (!scans.TryGetValue(file.FileId, out var scan))
            {
                scan = new MarkerScan { MediaFileId = file.FileId };
                _dbContext.MarkerScans.Add(scan);
            }

            var creditsStart = CreditsWindowStart(file);
            if (foundRecaps.TryGetValue(file.FileId, out var recap))
            {
                scan.RecapStartMs = (long)(recap.Start * 1000);
                scan.RecapEndMs = (long)(recap.End * 1000);
            }
            else
            {
                scan.RecapStartMs = null;
                scan.RecapEndMs = null;
            }

            if (foundIntros.TryGetValue(file.FileId, out var intro))
            {
                scan.IntroStartMs = (long)(intro.StartA * 1000);
                scan.IntroEndMs = (long)(intro.EndA * 1000);
            }
            else
            {
                scan.IntroStartMs = null;
                scan.IntroEndMs = null;
            }

            if (foundCredits.TryGetValue(file.FileId, out var credit))
            {
                var creditsStartMs = creditsStart.TotalMilliseconds + credit.StartA * 1000;
                var creditsEndMs = creditsStart.TotalMilliseconds + credit.EndA * 1000;
                scan.CreditsStartMs = (long)creditsStartMs;
                scan.CreditsEndMs = (long)Math.Min(file.DurationMs, creditsEndMs);
            }
            else
            {
                scan.CreditsStartMs = null;
                scan.CreditsEndMs = null;
            }

            scan.Error = errors.GetValueOrDefault(file.FileId);
            scan.Version = Version;
            scan.ScannedAt = _time.GetUtcNow();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Compared {Files} episode files of season {SeasonId}: {Recaps} recaps, {Intros} intros and {Credits} credits found",
            files.Count, seasonId, foundRecaps.Count, foundIntros.Count, foundCredits.Count);
        return true;
    }

    /// <inheritdoc />
    public async Task RemoveOrphansAsync(CancellationToken cancellationToken)
    {
        var root = _options.Value.ResolvePath();
        if (!Directory.Exists(root))
        {
            return;
        }

        var knownIds = await _dbContext.MediaFiles.AsNoTracking().Select(f => f.Id).ToListAsync(cancellationToken);
        var known = knownIds.ToHashSet();
        foreach (var file in Directory.EnumerateFiles(root, "*.fp*"))
        {
            // Named {fileId}-{intro|credits|full}-{seconds}.v{version}.fp. A file of a removed version of a video goes,
            // and so does one of an older fingerprint version and what a stopped write left.
            var name = Path.GetFileName(file);
            var removed = name.Length > 32 && Guid.TryParseExact(name[..32], "N", out var fileId) && !known.Contains(fileId);
            if (removed || !name.EndsWith(CacheSuffix, StringComparison.Ordinal))
            {
                File.Delete(file);
            }
        }
    }

    private async Task<Dictionary<Guid, AudioSpan>> FindRecapsAsync(List<EpisodeFile> files, Dictionary<Guid, uint[]> starts,
        IReadOnlyDictionary<Guid, SharedAudio> intros, Dictionary<Guid, string> errors, CancellationToken cancellationToken)
    {
        var whole = new Dictionary<Guid, uint[]?>();
        var found = new Dictionary<Guid, AudioSpan>();
        foreach (var file in files)
        {
            if (!starts.TryGetValue(file.FileId, out var start))
            {
                continue;
            }

            // Before the intro only: from there on the episodes share the intro itself
            var seconds = Math.Min(RecapMatch.Window.TotalSeconds, intros.TryGetValue(file.FileId, out var intro) ? intro.StartA : double.MaxValue);
            var points = Math.Min(start.Length, (int)(seconds / AudioMatch.SecondsPerPoint));
            var sources = files.Where(f => f.Episode < file.Episode)
                .GroupBy(f => f.Episode)
                .OrderByDescending(group => group.Key)
                .Take(RecapSources)
                .Select(group => group.First())
                .ToList();
            if (points * AudioMatch.SecondsPerPoint < RecapMatch.MinLength.TotalSeconds || sources.Count == 0)
            {
                continue;
            }

            var earlier = new List<uint[]>();
            foreach (var source in sources)
            {
                if (!whole.TryGetValue(source.FileId, out var sourcePoints))
                {
                    try
                    {
                        sourcePoints = await FingerprintAsync(source, "full", TimeSpan.Zero, TimeSpan.FromMilliseconds(source.DurationMs), cancellationToken);
                    }
                    catch (FingerprintException exception) when (exception.Failure == FingerprintFailure.Failed)
                    {
                        errors.TryAdd(source.FileId, exception.Message);
                        sourcePoints = null;
                    }

                    whole[source.FileId] = sourcePoints;
                }

                if (sourcePoints is not null)
                {
                    earlier.Add(sourcePoints);
                }
            }

            if (RecapMatch.Find(start[..points], earlier) is { } recap)
            {
                found[file.FileId] = recap;
            }
        }

        return found;
    }

    private Task<uint[]> FingerprintAsync(EpisodeFile file, bool intro, CancellationToken cancellationToken)
    {
        if (intro)
        {
            return FingerprintAsync(file, "intro", TimeSpan.Zero, IntroWindow(file), cancellationToken);
        }

        var start = CreditsWindowStart(file);
        return FingerprintAsync(file, "credits", start, TimeSpan.FromMilliseconds(file.DurationMs) - start, cancellationToken);
    }

    private async Task<uint[]> FingerprintAsync(EpisodeFile file, string kind, TimeSpan start, TimeSpan length, CancellationToken cancellationToken)
    {
        var root = _options.Value.ResolvePath();
        var cache = Path.Combine(root, $"{file.FileId:N}-{kind}-{(int)length.TotalSeconds}{CacheSuffix}");
        if (File.Exists(cache))
        {
            var bytes = await File.ReadAllBytesAsync(cache, cancellationToken);
            var cached = new uint[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, cached, 0, cached.Length * 4);
            return cached;
        }

        if (!File.Exists(file.Path))
        {
            throw new FingerprintException(FingerprintFailure.Failed, "The file is not on disk where the library says it is.");
        }

        var points = await _fingerprinter.FingerprintAsync(file.Path, file.AudioIndex, start, length, cancellationToken);
        Directory.CreateDirectory(root);
        var data = new byte[points.Length * 4];
        Buffer.BlockCopy(points, 0, data, 0, data.Length);
        // Written aside and moved in: a server stopped in the middle would leave a short fingerprint every later scan trusts
        var partial = cache + ".partial";
        await File.WriteAllBytesAsync(partial, data, cancellationToken);
        File.Move(partial, cache, overwrite: true);
        return points;
    }

    private TimeSpan IntroWindow(EpisodeFile file) =>
        TimeSpan.FromMilliseconds(Math.Min(_options.Value.IntroWindow.TotalMilliseconds, file.DurationMs * MaxIntroShare));

    private TimeSpan CreditsWindowStart(EpisodeFile file) =>
        TimeSpan.FromMilliseconds(file.DurationMs - Math.Min(_options.Value.CreditsWindow.TotalMilliseconds, file.DurationMs * MaxCreditsShare));
}
