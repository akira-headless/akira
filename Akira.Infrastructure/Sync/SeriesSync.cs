using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Library;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Sonarr;
using Akira.Infrastructure.Interfaces.Integrations;
using Akira.Infrastructure.Interfaces.Sync;

namespace Akira.Infrastructure.Sync;

public class SeriesSync : ISeriesSync
{
    // After this many series the database refused, the rest would go the same way
    private const int MaxSkipped = 20;

    private readonly AkiraDbContext _dbContext;
    private readonly IArrClientFactory _clients;
    private readonly ServerFolders _folders;
    private readonly SyncLock _syncLock;
    private readonly IOptions<ArrOptions> _options;
    private readonly ILogger<SeriesSync> _logger;

    public SeriesSync(
        AkiraDbContext dbContext,
        IArrClientFactory clients,
        ServerFolders folders,
        SyncLock syncLock,
        IOptions<ArrOptions> options,
        ILogger<SeriesSync> logger)
    {
        _dbContext = dbContext;
        _clients = clients;
        _folders = folders;
        _syncLock = syncLock;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncAsync(ArrInstanceOptions instance, CancellationToken cancellationToken)
    {
        var sonarr = _clients.CreateSonarr(instance);
        var version = _syncLock.Version;
        IReadOnlyList<SonarrSeries> seriesList;
        try
        {
            seriesList = await sonarr.GetSeriesAsync(cancellationToken);
        }
        catch (UpstreamException exception)
        {
            // Nothing is unlinked when the list could not be read
            return SyncResult.Failed(instance.Key, exception.Message);
        }

        var run = CreateRun(instance, sonarr, version);
        try
        {
            // A series at a time: its episodes are asked for outside the gate, written inside it and let go, so the
            // library is never tracked as a whole and a refresh or an action only waits for one series
            foreach (var series in seriesList)
            {
                if (!Accept(run, series))
                {
                    continue;
                }

                // Once another sync wrote, the list may be older than what that stored, and each series is asked for again
                var failure = await SyncOneAsync(run, series.Id, run.ListIsStale ? null : series, cancellationToken);
                if (failure is not null)
                {
                    run.Changes.Problem($"Kept {series.Title} unchanged: {failure}");
                }
            }

            await UnlinkMissingAsync(run, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // What the series before it saved stays, and is reported. Nothing more is unlinked after this.
            _logger.LogError(exception, "{Instance} sync stopped on an error nobody expected", instance.DisplayName);
            run.Changes.Problem($"The {instance.DisplayName} sync stopped halfway: {LibraryItems.Reason(exception)}");
        }

        return run.Changes.ToResult();
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncSeriesAsync(ArrInstanceOptions instance, int seriesId, CancellationToken cancellationToken)
    {
        var run = CreateRun(instance, _clients.CreateSonarr(instance), _syncLock.Version);
        var failure = await SyncOneAsync(run, seriesId, null, cancellationToken);
        return failure is null ? run.Changes.ToResult() : SyncResult.Failed(instance.Key, failure);
    }

    private SeriesRun CreateRun(ArrInstanceOptions instance, ISonarrClient sonarr, long version) =>
        new(instance, sonarr, instance.CreateLibraryPaths(_folders.Folders), new SyncChanges(instance.Key)) { Expected = version };

    private static bool Accept(SeriesRun run, SonarrSeries series)
    {
        var instance = run.Instance;
        if (!IsValid(series))
        {
            run.Changes.Problem($"Skipped {instance.DisplayName} series {series.Id} without an id or title.");
            return false;
        }

        var key = IdentityKeys.Series(series.TvdbId, instance.Key, series.Id);
        if (!run.Roots.Add(key))
        {
            run.Changes.Problem($"Skipped {instance.DisplayName} series {series.Id}, another series already has {key}.");
            return false;
        }

        return true;
    }

    private static bool IsValid(SonarrSeries series) => series.Id > 0 && !string.IsNullOrWhiteSpace(series.Title);

    /// <summary>
    /// Synchronizes one series, from the list's row of it or asked for by id. Returns why when Sonarr could not say whether it has it.
    /// </summary>
    private async Task<string?> SyncOneAsync(SeriesRun run, int seriesId, SonarrSeries? listed, CancellationToken cancellationToken)
    {
        // A row from the list is as old as the list, so it is held against what was written since
        var version = listed is null ? _syncLock.Version : run.Expected;
        SeriesAnswer answer;
        try
        {
            answer = await AskAsync(run.Sonarr, seriesId, listed, cancellationToken);
            answer = await _syncLock.EnterAsync(version, answer, () =>
            {
                run.ListIsStale = true;
                return AskAsync(run.Sonarr, seriesId, null, cancellationToken);
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Skip(run, listed?.Title ?? $"series {seriesId}", exception);
        }

        try
        {
            if (answer.Failure is not null)
            {
                return answer.Failure;
            }

            // A batch of refreshes shares one scope, and what the previous one tracked may be deleted by now
            _dbContext.ChangeTracker.Clear();
            await ApplyAsync(run, seriesId, answer, cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Skip(run, answer.Series?.Title ?? $"series {seriesId}", exception);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
            run.Expected = _syncLock.Advance();
            _syncLock.Gate.Release();
        }
    }

    // An answer the sync cannot work with, a row the database refuses: it costs this series, not the ones after it
    private string? Skip(SeriesRun run, string name, Exception exception)
    {
        if (++run.Skipped > MaxSkipped)
        {
            throw new InvalidOperationException($"{MaxSkipped} series were skipped, the last one on: {LibraryItems.Reason(exception)}",
                exception);
        }

        _logger.LogError(exception, "{Instance} {Series} was skipped on an error nobody expected", run.Instance.DisplayName, name);
        run.Changes.Problem($"Kept {name} unchanged: {LibraryItems.Reason(exception)}");
        return null;
    }

    private static async Task<SeriesAnswer> AskAsync(ISonarrClient sonarr, int seriesId, SonarrSeries? listed, CancellationToken cancellationToken)
    {
        var series = listed;
        if (series is null)
        {
            try
            {
                series = await sonarr.GetSeriesAsync(seriesId, cancellationToken);
            }
            catch (UpstreamException exception) when (exception.Failure == UpstreamFailure.NotFound)
            {
                // Deleted in Sonarr, so whatever this instance linked to it goes
                return new SeriesAnswer(null, null, null, null, null);
            }
            catch (UpstreamException exception)
            {
                return new SeriesAnswer(null, null, null, exception.Message, null);
            }
        }

        try
        {
            var episodes = await sonarr.GetEpisodesAsync(seriesId, cancellationToken);
            var files = await sonarr.GetEpisodeFilesAsync(seriesId, cancellationToken);
            return new SeriesAnswer(series, episodes, files, null, null);
        }
        catch (UpstreamException exception)
        {
            return new SeriesAnswer(series, null, null, null, exception.Message);
        }
    }

    private async Task ApplyAsync(SeriesRun run, int seriesId, SeriesAnswer answer, CancellationToken cancellationToken)
    {
        var instance = run.Instance;
        var series = answer.Series;
        if (series is not null && !IsValid(series))
        {
            run.Changes.Problem($"Skipped {instance.DisplayName} series {series.Id} without an id or title.");
            series = null;
        }

        // The series under its current key, and whatever this instance linked to that Sonarr id before,
        // each with its seasons and episodes. Only those are loaded, so only those can be unlinked.
        var instanceKey = instance.Key;
        var roots = await _dbContext.ArrLinks
            .Where(link => link.InstanceKey == instanceKey && link.ArrId == seriesId && link.MediaItem.Type == MediaItemType.Series)
            .Select(link => link.MediaItem.IdentityKey)
            .ToListAsync(cancellationToken);
        var seriesKey = series is null ? null : IdentityKeys.Series(series.TvdbId, instance.Key, series.Id);
        if (seriesKey is not null)
        {
            roots.Add(seriesKey);
            run.Roots.Add(seriesKey);
        }

        var unit = new SeriesUnit(run, []);
        foreach (var root in roots.Distinct())
        {
            foreach (var item in await LoadTreeAsync(root, cancellationToken))
            {
                unit.Items[item.IdentityKey] = item;
            }
        }

        string? kept = null;
        if (series is not null)
        {
            var seriesItem = Upsert(unit, seriesKey!, MediaItemType.Series, null, series.Title!.Trim(), series.Id);
            if (LibraryItems.Describes(seriesItem, instance.Key, _options.Value))
            {
                Describe(seriesItem, series, instance);
            }

            LibraryItems.AddedNoLaterThan(seriesItem, series.Added);
            LibraryItems.SyncImages(_dbContext, seriesItem, instance.Key, series.Id, series.Images);

            if (answer.Episodes is null || answer.Files is null)
            {
                // Its seasons and episodes stay exactly as they were rather than being unlinked
                run.Changes.Problem($"Kept {series.Title} unchanged: {answer.EpisodesFailure}");
                kept = seriesKey + ":";
            }
            else
            {
                SyncSeasonsAndEpisodes(unit, seriesItem, series, answer.Episodes, answer.Files);
            }
        }

        var unlinked = unit.Items.Values
            .Where(item => LibraryItems.IsLinked(item, instance.Key) && !unit.Seen.Contains(item.IdentityKey))
            .Where(item => kept is null || !item.IdentityKey.StartsWith(kept, StringComparison.Ordinal))
            .ToList();
        foreach (var item in unlinked)
        {
            LibraryItems.Unlink(_dbContext, item, instance.Key);
        }

        await run.Changes.SaveAsync(_dbContext, unlinked, cancellationToken);
    }

    private Task<List<MediaItem>> LoadTreeAsync(string root, CancellationToken cancellationToken)
    {
        var prefix = root + ":";
        return _dbContext.MediaItems
            .Include(item => item.Links)
            .Include(item => item.Files)
            .Include(item => item.Images)
            .Where(item => item.IdentityKey == root || item.IdentityKey.StartsWith(prefix))
            .ToListAsync(cancellationToken);
    }

    // What this instance linked and Sonarr no longer lists. Each series that is listed was handled on its own.
    private async Task UnlinkMissingAsync(SeriesRun run, CancellationToken cancellationToken)
    {
        var instance = run.Instance;
        try
        {
            // A refresh may have added a series the list is too old to have, so Sonarr is asked again before anything goes.
            // Roots only grow, so an older list can keep a series one more sync, never take one away.
            var version = _syncLock.Version;
            IReadOnlyList<SonarrSeries> listed = run.ListIsStale || version != run.Expected
                ? await run.Sonarr.GetSeriesAsync(cancellationToken)
                : [];
            listed = await _syncLock.EnterAsync(version, listed, () => run.Sonarr.GetSeriesAsync(cancellationToken), cancellationToken);
            foreach (var series in listed.Where(IsValid))
            {
                run.Roots.Add(IdentityKeys.Series(series.TvdbId, instance.Key, series.Id));
            }
        }
        catch (UpstreamException exception)
        {
            run.Changes.Problem($"Nothing {instance.DisplayName} dropped was unlinked: {exception.Message}");
            return;
        }

        try
        {
            var instanceKey = instance.Key;
            var linked = await _dbContext.MediaItems
                .Where(item => item.Type == MediaItemType.Series && item.Links.Any(link => link.InstanceKey == instanceKey))
                .Select(item => item.IdentityKey)
                .ToListAsync(cancellationToken);

            foreach (var root in linked.Where(key => !run.Roots.Contains(key)))
            {
                _dbContext.ChangeTracker.Clear();
                var tree = await LoadTreeAsync(root, cancellationToken);
                var unlinked = tree.Where(item => LibraryItems.IsLinked(item, instanceKey)).ToList();
                foreach (var item in unlinked)
                {
                    LibraryItems.Unlink(_dbContext, item, instanceKey);
                }

                await run.Changes.SaveAsync(_dbContext, unlinked, cancellationToken);
            }
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
            run.Expected = _syncLock.Advance();
            _syncLock.Gate.Release();
        }
    }

    private void SyncSeasonsAndEpisodes(
        SeriesUnit unit,
        MediaItem seriesItem,
        SonarrSeries series,
        IReadOnlyList<SonarrEpisode> episodes,
        IReadOnlyList<SonarrEpisodeFile> files)
    {
        var instance = unit.Run.Instance;
        var changes = unit.Run.Changes;

        // The first of a season or file listed twice counts
        var seasonImages = new Dictionary<int, List<ArrImage>?>();
        foreach (var season in series.Seasons ?? [])
        {
            seasonImages.TryAdd(season.SeasonNumber, season.Images);
        }

        var seasonNumbers = seasonImages.Keys
            .Concat(episodes.Select(episode => episode.SeasonNumber))
            .Where(number => number >= 0)
            .Distinct()
            .Order();

        var seasons = new Dictionary<int, MediaItem>();
        foreach (var seasonNumber in seasonNumbers)
        {
            var key = IdentityKeys.Season(seriesItem.IdentityKey, seasonNumber);
            unit.Seen.Add(key);

            var title = seasonNumber == 0 ? "Specials" : $"Season {seasonNumber}";
            var season = Upsert(unit, key, MediaItemType.Season, seriesItem, title, null);
            season.Title = title;
            season.SortTitle = SearchText.Fold(title);
            season.SeasonNumber = seasonNumber;
            season.TvdbId = seriesItem.TvdbId;

            // Season artwork is fetched from the artwork host, the cache route is keyed by series
            LibraryItems.SyncImages(_dbContext, season, instance.Key, null, seasonImages.GetValueOrDefault(seasonNumber));
            seasons[seasonNumber] = season;
        }

        var filesById = new Dictionary<int, SonarrEpisodeFile>();
        foreach (var file in files)
        {
            filesById.TryAdd(file.Id, file);
        }

        foreach (var episode in episodes)
        {
            if (episode.Id <= 0 || episode.SeasonNumber < 0 || episode.EpisodeNumber < 0)
            {
                changes.Problem($"Skipped {instance.DisplayName} episode {episode.Id} of {series.Title} with an invalid number.");
                continue;
            }

            var key = IdentityKeys.Episode(seriesItem.IdentityKey, episode.SeasonNumber, episode.EpisodeNumber);
            if (!unit.Seen.Add(key))
            {
                changes.Problem($"Skipped {instance.DisplayName} episode {episode.Id}, another episode already has {key}.");
                continue;
            }

            var title = LibraryItems.NullIfEmpty(episode.Title) ?? $"Episode {episode.EpisodeNumber}";
            var item = Upsert(unit, key, MediaItemType.Episode, seasons[episode.SeasonNumber], title, episode.Id);
            item.SeasonNumber = episode.SeasonNumber;
            item.EpisodeNumber = episode.EpisodeNumber;
            if (LibraryItems.Describes(item, instance.Key, _options.Value))
            {
                item.Title = title;
                item.SortTitle = SearchText.Fold(title);
                item.Overview = LibraryItems.NullIfEmpty(episode.Overview);
                item.AirDate = episode.AirDate;
                item.RuntimeMinutes = LibraryItems.Positive(episode.Runtime) ?? seriesItem.RuntimeMinutes;
                item.TvdbId = LibraryItems.Positive(episode.TvdbId);
            }

            SonarrEpisodeFile? file = null;
            if (episode.HasFile && episode.EpisodeFileId is { } fileId)
            {
                file = filesById.GetValueOrDefault(fileId);
            }

            if (episode.HasFile && file is null)
            {
                changes.Problem($"{instance.DisplayName} episode {episode.Id} of {series.Title} has file " +
                                $"{episode.EpisodeFileId}, which it did not list.");
            }

            // The earliest copy of the episode decides, so a 4K copy added later does not make it "new"
            if (file?.DateAdded is { } fileAdded)
            {
                LibraryItems.AddedNoLaterThan(item, fileAdded);
            }

            if (!LibraryItems.SyncFile(_dbContext, item, instance.Key, file?.Id, file?.Path, file?.Size ?? 0, file?.DateAdded, unit.Run.Paths))
            {
                changes.Problem($"Skipped the file of {instance.DisplayName} episode {episode.Id} of {series.Title}: {file!.Path} is " +
                                "outside the path mappings or in the server's own folders.");
            }

            LibraryItems.SyncImages(_dbContext, item, instance.Key, null, episode.Images);
        }
    }

    private MediaItem Upsert(
        SeriesUnit unit,
        string key,
        MediaItemType type,
        MediaItem? parent,
        string title,
        int? arrId)
    {
        unit.Seen.Add(key);
        if (!unit.Items.TryGetValue(key, out var item))
        {
            item = new MediaItem
            {
                Type = type,
                IdentityKey = key,
                Title = title,
                SortTitle = SearchText.Fold(title),
                Parent = parent,
                ParentId = parent?.Id
            };
            _dbContext.MediaItems.Add(item);
            unit.Items[key] = item;
        }
        else if (parent is not null && item.ParentId != parent.Id)
        {
            item.Parent = parent;
            item.ParentId = parent.Id;
        }

        LibraryItems.Link(_dbContext, item, unit.Run.Instance.Key, arrId);
        return item;
    }

    private void Describe(MediaItem item, SonarrSeries series, ArrInstanceOptions instance)
    {
        var description = new ItemDescription(series.Title!, series.SortTitle, series.Year, series.Overview, series.Runtime, series.TmdbId,
            series.ImdbId, series.OriginalLanguage, series.Genres, series.Certification);
        if (!LibraryItems.Describe(item, description))
        {
            _logger.LogWarning("{Instance} language '{Language}' of {Title} has no ISO 639-2 code, original language left empty",
                instance.DisplayName, series.OriginalLanguage?.Name, item.Title);
        }

        item.TvdbId = LibraryItems.Positive(series.TvdbId);
        item.Rating = LibraryItems.Score(series.Ratings);
    }

    /// <summary>
    /// Represents one sync of a Sonarr instance. Roots holds the keys of the series Sonarr still has, and Expected
    /// the version of the lock when this sync last read or wrote.
    /// </summary>
    private sealed record SeriesRun(ArrInstanceOptions Instance, ISonarrClient Sonarr, LibraryPaths Paths, SyncChanges Changes)
    {
        public HashSet<string> Roots { get; } = [];
        public long Expected { get; set; }
        public bool ListIsStale { get; set; }
        public int Skipped { get; set; }
    }

    /// <summary>
    /// Represents one series being written: the rows loaded for it, and in Seen the identity keys Sonarr still has.
    /// </summary>
    private sealed record SeriesUnit(SeriesRun Run, Dictionary<string, MediaItem> Items)
    {
        public HashSet<string> Seen { get; } = [];
    }

    /// <summary>
    /// Represents what Sonarr answered about one series. Series is null when it no longer has it, Failure says why
    /// that could not be read, and EpisodesFailure why its episodes or files could not.
    /// </summary>
    private sealed record SeriesAnswer(
        SonarrSeries? Series,
        IReadOnlyList<SonarrEpisode>? Episodes,
        IReadOnlyList<SonarrEpisodeFile>? Files,
        string? Failure,
        string? EpisodesFailure);
}
