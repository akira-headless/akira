using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Library;
using Akira.Server.Endpoints;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Items;
using Akira.Server.Models.Playback;

namespace Akira.Server.Services;

public class LibraryService : ILibraryService
{
    // One definition of what an item looks like in a list, including whether it can be played.
    // A series or season is available when at least one of its episodes has a file.
    private static readonly Expression<Func<MediaItem, ItemRow>> ToRow = item => new ItemRow
    {
        Id = item.Id,
        Type = item.Type,
        IdentityKey = item.IdentityKey,
        Title = item.Title,
        SortTitle = item.SortTitle,
        SearchTitle = item.SearchTitle,
        Year = item.Year,
        Overview = item.Overview,
        TmdbId = item.TmdbId,
        TvdbId = item.TvdbId,
        ImdbId = item.ImdbId,
        ParentId = item.ParentId,
        SeriesId = item.Type == MediaItemType.Episode ? item.Parent!.ParentId
            : item.Type == MediaItemType.Season ? item.ParentId
            : null,
        SeriesTitle = item.Type == MediaItemType.Episode ? item.Parent!.Parent!.Title
            : item.Type == MediaItemType.Season ? item.Parent!.Title
            : null,
        SeasonNumber = item.SeasonNumber,
        EpisodeNumber = item.EpisodeNumber,
        AirDate = item.AirDate,
        RuntimeMinutes = item.RuntimeMinutes,
        ImdbRating = item.ImdbRating,
        Rating = item.Rating,
        AddedAt = item.AddedAt,
        // A series counts as new again when an episode arrives
        LatestAt = item.Type == MediaItemType.Series
            ? item.Children.SelectMany(season => season.Children).Max(episode => episode.AddedAt) ?? item.AddedAt
            : item.AddedAt,
        IsAvailable = item.Files.Any()
            || item.Children.Any(child => child.Files.Any() || child.Children.Any(grandchild => grandchild.Files.Any()))
    };

    private readonly AkiraDbContext _dbContext;
    private readonly IOptions<ArrOptions> _options;

    public LibraryService(
        AkiraDbContext dbContext,
        IOptions<ArrOptions> options)
    {
        _dbContext = dbContext;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<PagedResponse<ItemSummary>> ListAsync(ItemListQuery query, Guid userId, CancellationToken cancellationToken)
    {
        var rows = Filter(query, userId);
        var total = await rows.CountAsync(cancellationToken);
        var page = await Sort(rows, query.Sort, query.Order)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        var summaries = await ToSummariesAsync(page, userId, cancellationToken);
        return new PagedResponse<ItemSummary>(summaries, total, query.Offset, query.Limit);
    }

    /// <inheritdoc />
    public async Task<ItemLettersResponse> LettersAsync(ItemListQuery query, Guid userId, CancellationToken cancellationToken)
    {
        // The same query and order as the A to Z list, so every offset is where that list has the letter
        var rows = Filter(query, userId);
        var titles = await Sort(rows, ItemSort.Title, SortOrder.Asc)
            .Select(row => row.SortTitle)
            .ToListAsync(cancellationToken);
        var letters = TitleLetters.Index(titles).Select(entry => new ItemLetter(entry.Letter, entry.Offset, entry.Count)).ToList();
        return new ItemLettersResponse(letters, titles.Count);
    }

    private IQueryable<ItemRow> Filter(ItemListQuery query, Guid userId)
    {
        var items = _dbContext.MediaItems.AsNoTracking();
        items = query.Type is { } type
            ? items.Where(item => item.Type == type)
            : items.Where(item => item.Type == MediaItemType.Movie || item.Type == MediaItemType.Series);

        var rows = items.Select(ToRow);
        if (!query.IncludeUnavailable)
        {
            rows = rows.Where(row => row.IsAvailable);
        }

        // A movie the user has not finished, or a series with an episode to play they have not finished
        if (query.Unwatched)
        {
            var played = _dbContext.WatchStates.Where(state => state.UserId == userId && state.IsPlayed).Select(state => state.IdentityKey);
            rows = rows.Where(row => row.Type == MediaItemType.Series
                ? _dbContext.MediaItems.Any(episode => episode.Type == MediaItemType.Episode && episode.Parent!.ParentId == row.Id
                                                      && episode.Files.Any() && !played.Contains(episode.IdentityKey))
                : !played.Contains(row.IdentityKey));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // The title as written, or folded, so amelie finds Amelie and Amélie on SQLite too
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            var folded = $"%{EscapeLike(SearchText.Fold(query.Search.Trim()))}%";
            rows = rows.Where(row => EF.Functions.Like(row.Title, pattern, "\\")
                                     || (row.SearchTitle != null && EF.Functions.Like(row.SearchTitle, folded, "\\")));
        }

        // Sort titles are stored folded, so the prefix is folded the same way
        if (!string.IsNullOrWhiteSpace(query.TitlePrefix))
        {
            var prefix = $"{EscapeLike(SearchText.Fold(query.TitlePrefix.Trim()))}%";
            rows = rows.Where(row => EF.Functions.Like(row.SortTitle, prefix, "\\"));
        }

        return rows;
    }

    /// <inheritdoc />
    public async Task<ItemDetail?> GetAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.MediaItems.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(ToRow)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var details = await _dbContext.MediaItems.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Overview,
                // Episodes and seasons report the language of their series
                OriginalLanguage = item.OriginalLanguage ?? item.Parent!.OriginalLanguage ?? item.Parent!.Parent!.OriginalLanguage,
                item.TmdbId,
                item.TvdbId,
                item.ImdbId,
                item.ImdbRating,
                item.ImdbVotes,
                item.TmdbRating,
                item.Rating,
                item.TrailerYoutubeId,
                Genres = item.Genres ?? item.Parent!.Genres ?? item.Parent!.Parent!.Genres,
                Certification = item.Certification ?? item.Parent!.Certification ?? item.Parent!.Parent!.Certification,
                Instances = item.Links.Select(link => link.InstanceKey).ToList(),
                Files = item.Files
                    .Select(file => new
                    {
                        file.Id,
                        file.InstanceKey,
                        file.SizeBytes,
                        file.Container,
                        file.AddedAt,
                        file.DurationMs,
                        file.ProbeStatus,
                        Video = file.Streams
                            .Where(stream => stream.Kind == StreamKind.Video)
                            .OrderBy(stream => stream.SortOrder)
                            .Select(stream => new VersionVideo(stream.Codec, stream.Width, stream.Height, stream.HdrFormat, stream.Profile,
                                stream.BitDepth, stream.FrameRate))
                            .FirstOrDefault(),
                        Audio = file.Streams
                            .Where(stream => stream.Kind == StreamKind.Audio)
                            .OrderBy(stream => stream.SortOrder)
                            .Select(stream => new VersionTrack(stream.Id, stream.Language, stream.Codec, stream.Profile, stream.Channels, stream.Title,
                                stream.IsDefault, stream.IsForced, stream.IsHearingImpaired, stream.IsCommentary, stream.IsExternal))
                            .ToList(),
                        Subtitles = file.Streams
                            .Where(stream => stream.Kind == StreamKind.Subtitle)
                            .OrderBy(stream => stream.SortOrder)
                            .Select(stream => new VersionTrack(stream.Id, stream.Language, stream.Codec, stream.Profile, stream.Channels, stream.Title,
                                stream.IsDefault, stream.IsForced, stream.IsHearingImpaired, stream.IsCommentary, stream.IsExternal))
                            .ToList()
                    })
                    .ToList()
            })
            .SingleAsync(cancellationToken);

        // Versions in configuration order, so the first configured instance is listed first everywhere
        var instances = _options.Value;
        var versions = details.Files
            .OrderBy(file => instances.OrderOf(file.InstanceKey))
            .Select(file => new ItemVersion(file.Id, file.InstanceKey, InstanceName(instances, file.InstanceKey), file.SizeBytes, file.Container,
                file.AddedAt, file.DurationMs, file.ProbeStatus, file.Video, file.Audio, file.Subtitles))
            .ToList();

        var holding = details.Instances
            .OrderBy(key => instances.OrderOf(key))
            .Select(key => new ItemInstance(key, InstanceName(instances, key)))
            .ToList();

        var summaries = await ToSummariesAsync([row], userId, cancellationToken);
        var summary = summaries[0];
        var externalIds = new ExternalIds(details.TmdbId, details.TvdbId, details.ImdbId);
        string[] genres = details.Genres?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        var trailerUrl = details.TrailerYoutubeId is { } trailer ? $"https://www.youtube.com/watch?v={trailer}" : null;
        var info = new ItemInfo(details.ImdbRating, details.ImdbVotes, details.TmdbRating, details.Rating, genres, details.Certification,
            trailerUrl);
        return new ItemDetail(
            row.Id, row.Type, row.Title, row.SortTitle, row.Year, details.Overview, row.ParentId, row.SeriesId,
            row.SeriesTitle, row.SeasonNumber, row.EpisodeNumber, row.AirDate, row.RuntimeMinutes,
            details.OriginalLanguage, externalIds,
            row.AddedAt, row.IsAvailable, holding, versions, summary.Images, summary.UserState, summary.UnplayedCount,
            info);
    }

    /// <inheritdoc />
    public async Task<PagedResponse<ItemSummary>?> ListChildrenAsync(Guid id, PageQuery query, Guid userId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.MediaItems.AnyAsync(item => item.Id == id, cancellationToken))
        {
            return null;
        }

        var rows = _dbContext.MediaItems.AsNoTracking()
            .Where(item => item.ParentId == id)
            .Select(ToRow);
        if (!query.IncludeUnavailable)
        {
            rows = rows.Where(row => row.IsAvailable);
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderBy(row => row.SeasonNumber)
            .ThenBy(row => row.EpisodeNumber)
            .ThenBy(row => row.SortTitle)
            .ThenBy(row => row.Id)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        var summaries = await ToSummariesAsync(page, userId, cancellationToken);
        return new PagedResponse<ItemSummary>(summaries, total, query.Offset, query.Limit);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemSummary>> ContinueWatchingAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        // Watch state outlives items, so only what is still in the library and playable is listed; page on through the
        // states until enough of them are, as many recent ones may be of items that are gone
        var found = new List<ItemRow>();
        var batch = Math.Max(limit * 2, 20);
        for (var skip = 0; found.Count < limit; skip += batch)
        {
            var started = await _dbContext.WatchStates.AsNoTracking()
                .Where(state => state.UserId == userId && !state.IsPlayed && state.PositionMs > 0)
                .OrderByDescending(state => state.LastPlayedAt)
                .ThenBy(state => state.IdentityKey)
                .Select(state => state.IdentityKey)
                .Skip(skip)
                .Take(batch)
                .ToListAsync(cancellationToken);
            var rows = await _dbContext.MediaItems.AsNoTracking()
                .Where(item => started.Contains(item.IdentityKey) && item.Files.Any())
                .Select(ToRow)
                .ToListAsync(cancellationToken);
            found.AddRange(rows.OrderBy(row => started.IndexOf(row.IdentityKey)));
            if (started.Count < batch)
            {
                break;
            }
        }

        var shown = found.Take(limit).ToList();
        return await ToSummariesAsync(shown, userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemSummary>> NextUpAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        var states = _dbContext.WatchStates.AsNoTracking().Where(state => state.UserId == userId);
        var played = states.Where(state => state.IsPlayed).Select(state => state.IdentityKey);
        var episodes = _dbContext.MediaItems.AsNoTracking().Where(item => item.Type == MediaItemType.Episode);

        // Joined, grouped and searched by the database. What a user watched over the years and the episodes of
        // every series they ever started are too many to bring here for each home screen.
        var watched = episodes.Join(states.Where(state => state.IsPlayed || state.PositionMs > 0),
            episode => episode.IdentityKey,
            state => state.IdentityKey,
            (episode, state) => new
            {
                SeriesId = episode.Parent!.ParentId,
                episode.SeasonNumber,
                episode.EpisodeNumber,
                state.IsPlayed,
                state.LastPlayedAt
            });

        // What can be next: not a special, with a file, not finished
        var open = episodes.Where(episode => episode.SeasonNumber > 0 && episode.Files.Any() && !played.Contains(episode.IdentityKey));

        var found = new List<ItemRow>();
        var batch = Math.Max(limit * 2, 20);
        for (var skip = 0; found.Count < limit; skip += batch)
        {
            var recent = await watched
                .GroupBy(episode => episode.SeriesId)
                .Select(series => new { SeriesId = series.Key, LastPlayedAt = series.Max(episode => episode.LastPlayedAt) })
                .Where(series => open.Any(episode => episode.Parent!.ParentId == series.SeriesId))
                .OrderByDescending(series => series.LastPlayedAt)
                .ThenBy(series => series.SeriesId)
                .Skip(skip)
                .Take(batch)
                .ToListAsync(cancellationToken);

            // Per series, the episode the user watched most recently decides where they are. The times of this
            // batch find those rows, and a time two series share is told apart below.
            var seriesIds = recent.Select(series => series.SeriesId).ToList();
            var times = recent.Select(series => series.LastPlayedAt).Distinct().ToList();
            var latest = await watched
                .Where(episode => seriesIds.Contains(episode.SeriesId) && times.Contains(episode.LastPlayedAt))
                .ToListAsync(cancellationToken);

            foreach (var series in recent)
            {
                // A season marked played stamps its episodes with one time, and the last of them is where the user is
                var position = latest
                    .Where(episode => episode.SeriesId == series.SeriesId && episode.LastPlayedAt == series.LastPlayedAt)
                    .OrderBy(episode => episode.SeasonNumber)
                    .ThenBy(episode => episode.EpisodeNumber)
                    .LastOrDefault();
                if (position is null)
                {
                    continue;
                }

                // An episode stopped halfway is still the next one, a finished one hands over to the one after it
                var seriesId = series.SeriesId;
                var season = position.SeasonNumber;
                var from = position.IsPlayed ? position.EpisodeNumber + 1 : position.EpisodeNumber;
                var next = await open
                    .Where(episode => episode.Parent!.ParentId == seriesId)
                    .Where(episode => episode.SeasonNumber > season || (episode.SeasonNumber == season && episode.EpisodeNumber >= from))
                    .OrderBy(episode => episode.SeasonNumber)
                    .ThenBy(episode => episode.EpisodeNumber)
                    .Select(ToRow)
                    .FirstOrDefaultAsync(cancellationToken);

                // A series watched past its last open episode has none, so the limit counts the series that do
                if (next is not null)
                {
                    found.Add(next);
                    if (found.Count == limit)
                    {
                        break;
                    }
                }
            }

            if (recent.Count < batch)
            {
                break;
            }
        }

        return await ToSummariesAsync(found, userId, cancellationToken);
    }

    private static IQueryable<ItemRow> Sort(IQueryable<ItemRow> rows, ItemSort sort, SortOrder order)
    {
        var descending = order == SortOrder.Desc;
        return sort switch
        {
            ItemSort.Added => descending
                ? rows.OrderByDescending(row => row.LatestAt).ThenBy(row => row.SortTitle).ThenBy(row => row.Id)
                : rows.OrderBy(row => row.LatestAt).ThenBy(row => row.SortTitle).ThenBy(row => row.Id),
            ItemSort.Year => descending
                ? rows.OrderByDescending(row => row.Year).ThenBy(row => row.SortTitle).ThenBy(row => row.Id)
                : rows.OrderBy(row => row.Year).ThenBy(row => row.SortTitle).ThenBy(row => row.Id),
            _ => descending
                ? rows.OrderByDescending(row => row.SortTitle).ThenByDescending(row => row.Id)
                : rows.OrderBy(row => row.SortTitle).ThenBy(row => row.Id)
        };
    }

    private async Task<IReadOnlyList<ItemSummary>> ToSummariesAsync(List<ItemRow> rows, Guid userId, CancellationToken cancellationToken)
    {
        var ids = rows.Select(row => row.Id).ToList();
        var images = await GetImagesAsync(ids, cancellationToken);
        var keys = rows.Select(row => row.IdentityKey).ToList();
        var states = await GetStatesAsync(userId, keys, cancellationToken);
        var parents = rows.Where(row => row.Type is MediaItemType.Series or MediaItemType.Season).ToList();
        var unplayed = await GetUnplayedCountsAsync(userId, parents, cancellationToken);

        return rows
            .Select(row =>
            {
                var rowImages = images.GetValueOrDefault(row.Id, []);
                int? unplayedCount = unplayed.TryGetValue(row.Id, out var count) ? count : null;
                return new ItemSummary(
                    row.Id, row.Type, row.Title, row.SortTitle, row.Year, row.Overview, new ExternalIds(row.TmdbId, row.TvdbId, row.ImdbId),
                    row.ParentId, row.SeriesId, row.SeriesTitle,
                    row.SeasonNumber, row.EpisodeNumber, row.AirDate, row.RuntimeMinutes, row.AddedAt, row.IsAvailable,
                    rowImages, states.GetValueOrDefault(row.IdentityKey), unplayedCount, row.ImdbRating, row.Rating);
            })
            .ToList();
    }

    private async Task<Dictionary<string, UserState>> GetStatesAsync(Guid userId, List<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        return await _dbContext.WatchStates.AsNoTracking()
            .Where(state => state.UserId == userId && keys.Contains(state.IdentityKey))
            .ToDictionaryAsync(state => state.IdentityKey,
                state => new UserState(state.PositionMs, state.IsPlayed, state.PlayCount, state.LastPlayedAt), cancellationToken);
    }

    // Available episodes the user has not finished, for the badge on a series or season
    private async Task<Dictionary<Guid, int>> GetUnplayedCountsAsync(Guid userId, List<ItemRow> parents, CancellationToken cancellationToken)
    {
        if (parents.Count == 0)
        {
            return [];
        }

        // Counted by the database, so neither the episodes nor the states of a long series come here
        var played = _dbContext.WatchStates.Where(state => state.UserId == userId && state.IsPlayed).Select(state => state.IdentityKey);
        var unplayed = _dbContext.MediaItems.AsNoTracking()
            .Where(item => item.Type == MediaItemType.Episode && item.Files.Any() && !played.Contains(item.IdentityKey));

        var counts = parents.ToDictionary(parent => parent.Id, _ => 0);
        var seasonIds = parents.Where(parent => parent.Type == MediaItemType.Season).Select(parent => (Guid?)parent.Id).ToList();
        if (seasonIds.Count > 0)
        {
            var bySeason = await unplayed
                .Where(episode => seasonIds.Contains(episode.ParentId))
                .GroupBy(episode => episode.ParentId)
                .Select(season => new { Id = season.Key, Count = season.Count() })
                .ToListAsync(cancellationToken);
            foreach (var season in bySeason)
            {
                counts[season.Id!.Value] = season.Count;
            }
        }

        var seriesIds = parents.Where(parent => parent.Type == MediaItemType.Series).Select(parent => (Guid?)parent.Id).ToList();
        if (seriesIds.Count > 0)
        {
            var bySeries = await unplayed
                .Where(episode => seriesIds.Contains(episode.Parent!.ParentId))
                .GroupBy(episode => episode.Parent!.ParentId)
                .Select(series => new { Id = series.Key, Count = series.Count() })
                .ToListAsync(cancellationToken);
            foreach (var series in bySeries)
            {
                counts[series.Id!.Value] = series.Count;
            }
        }

        return counts;
    }

    private async Task<Dictionary<Guid, IReadOnlyList<ItemImage>>> GetImagesAsync(List<Guid> itemIds, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var images = await _dbContext.MediaImages.AsNoTracking()
            .Where(image => itemIds.Contains(image.MediaItemId))
            .Select(image => new { image.MediaItemId, image.Kind, image.BlurHash })
            .ToListAsync(cancellationToken);

        // Several instances can each hold the same kind, the client sees it once; an empty hash is one that could not be made
        return images
            .OrderBy(image => string.IsNullOrEmpty(image.BlurHash))
            .DistinctBy(image => (image.MediaItemId, image.Kind))
            .GroupBy(image => image.MediaItemId)
            .ToDictionary(
                group => group.Key,
                IReadOnlyList<ItemImage> (group) => group
                    .OrderBy(image => image.Kind)
                    .Select(image =>
                    {
                        var blurHash = string.IsNullOrEmpty(image.BlurHash) ? null : image.BlurHash;
                        return new ItemImage(image.Kind, ItemRoutes.ImageUrl(group.Key, image.Kind), blurHash);
                    })
                    .ToList());
    }

    private static string InstanceName(ArrOptions instances, string key) =>
        instances.Instances.FirstOrDefault(instance => instance.Key == key)?.DisplayName ?? key;

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private sealed class ItemRow
    {
        public Guid Id { get; init; }
        public double? ImdbRating { get; init; }
        public double? Rating { get; init; }
        public MediaItemType Type { get; init; }
        public string IdentityKey { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string SortTitle { get; init; } = string.Empty;
        public string? SearchTitle { get; init; }
        public int? Year { get; init; }
        public string? Overview { get; init; }
        public int? TmdbId { get; init; }
        public int? TvdbId { get; init; }
        public string? ImdbId { get; init; }
        public Guid? ParentId { get; init; }
        public Guid? SeriesId { get; init; }
        public string? SeriesTitle { get; init; }
        public int? SeasonNumber { get; init; }
        public int? EpisodeNumber { get; init; }
        public DateOnly? AirDate { get; init; }
        public int? RuntimeMinutes { get; init; }
        public DateTimeOffset? AddedAt { get; init; }
        public DateTimeOffset? LatestAt { get; init; }
        public bool IsAvailable { get; init; }
    }
}
