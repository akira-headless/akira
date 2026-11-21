using Akira.Contracts.Accounts;
using Akira.Contracts.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Items;
using Akira.Server.Models.Playback;
using Akira.Server.Services;
using ItemSort = Akira.Server.Models.Items.ItemSort;

namespace Akira.Server.Bridges;

/// <summary>
/// Represents the library as bridges see it, on top of the same services as the API, so what is available and how it sorts is
/// decided in one place.
/// </summary>
public sealed class LibraryAccess : ILibraryAccess
{
    private const int MaxPage = 200;

    // Well under the parameters one SQLite statement takes
    private const int MaxIdsPerQuery = 500;

    private readonly ILibraryService _library;
    private readonly IImageService _images;
    private readonly AkiraDbContext _dbContext;
    private readonly IOptions<ArrOptions> _arrOptions;

    public LibraryAccess(
        ILibraryService library,
        IImageService images,
        AkiraDbContext dbContext,
        IOptions<ArrOptions> arrOptions)
    {
        _library = library;
        _images = images;
        _dbContext = dbContext;
        _arrOptions = arrOptions;
    }

    /// <inheritdoc />
    public async Task<ItemPage> ListAsync(Caller caller, ItemQuery query, CancellationToken cancellationToken)
    {
        var type = query.Kind == ItemKind.Series ? MediaItemType.Series : MediaItemType.Movie;
        var byTitle = query.Order == ItemOrder.Title;
        var sort = byTitle ? ItemSort.Title : ItemSort.Added;

        // Titles run A to Z and additions newest first, unless the query turns that around
        var ascending = byTitle != query.Reverse;
        var order = ascending ? SortOrder.Asc : SortOrder.Desc;
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, MaxPage);

        var listQuery = new ItemListQuery(type, query.Search, sort, order, IncludeUnavailable: false, offset, limit, query.Unplayed, query.Prefix);
        if (query.Letter is { } letter)
        {
            return await LetterAsync(caller, listQuery, letter, cancellationToken);
        }

        var page = await _library.ListAsync(listQuery, caller.UserId, cancellationToken);
        return new ItemPage(page.Items.Select(ToItem).ToList(), page.Total);
    }

    // The titles of one letter are a stretch of the A to Z list, where the letters say it starts
    private async Task<ItemPage> LetterAsync(Caller caller, ItemListQuery query, string letter, CancellationToken cancellationToken)
    {
        var byTitle = query with { Sort = ItemSort.Title, Order = SortOrder.Asc };
        var letters = await _library.LettersAsync(byTitle, caller.UserId, cancellationToken);
        var found = letters.Letters.FirstOrDefault(entry => string.Equals(entry.Letter, letter.Trim(), StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            return new ItemPage([], 0);
        }

        var skip = Math.Min(query.Offset, found.Count);
        var take = Math.Min(query.Limit, found.Count - skip);
        if (take == 0)
        {
            return new ItemPage([], found.Count);
        }

        var page = await _library.ListAsync(byTitle with { Offset = found.Offset + skip, Limit = take }, caller.UserId, cancellationToken);
        return new ItemPage(page.Items.Select(ToItem).ToList(), found.Count);
    }

    /// <inheritdoc />
    public async Task<LibraryItem?> GetAsync(Caller caller, Guid id, CancellationToken cancellationToken)
    {
        var item = await _library.GetAsync(id, caller.UserId, cancellationToken);
        if (item is not { IsAvailable: true })
        {
            return null;
        }

        return ToItem(item);
    }

    /// <inheritdoc />
    public async Task<LibraryItem?> FindAsync(Caller caller, ExternalIdKind idKind, string id, ItemKind kind, CancellationToken cancellationToken)
    {
        var type = kind == ItemKind.Series ? MediaItemType.Series : MediaItemType.Movie;
        var items = _dbContext.MediaItems.AsNoTracking().Where(item => item.Type == type);
        var number = int.TryParse(id, out var parsed) ? parsed : (int?)null;
        items = idKind switch
        {
            ExternalIdKind.Imdb => items.Where(item => item.ImdbId == id.ToLowerInvariant()),
            ExternalIdKind.Tmdb when number is not null => items.Where(item => item.TmdbId == number),
            ExternalIdKind.Tvdb when number is not null => items.Where(item => item.TvdbId == number),
            _ => items.Where(_ => false)
        };

        var match = await items.Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);
        if (match is not { } found)
        {
            return null;
        }

        return await GetAsync(caller, found, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryItem>> ChildrenAsync(Caller caller, Guid parentId, CancellationToken cancellationToken)
    {
        var children = new List<LibraryItem>();
        for (var offset = 0; ; offset += MaxPage)
        {
            var page = await _library.ListChildrenAsync(parentId, new PageQuery(IncludeUnavailable: false, offset, MaxPage), caller.UserId,
                cancellationToken);
            if (page is null)
            {
                return children;
            }

            children.AddRange(page.Items.Select(ToItem));
            if (children.Count >= page.Total || page.Items.Count == 0)
            {
                return children;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaVersion>> VersionsAsync(Caller caller, Guid itemId, CancellationToken cancellationToken)
    {
        var versions = await VersionsAsync(caller, [itemId], cancellationToken);
        return versions.GetValueOrDefault(itemId) ?? [];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<MediaVersion>>> VersionsAsync(Caller caller, IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken)
    {
        // The files and their tracks in two reads a batch, however many items a listing shows: a renderer browsing
        // a folder of 200 asked four reads a version before
        var files = new List<MediaFile>();
        foreach (var batch in itemIds.Distinct().Chunk(MaxIdsPerQuery))
        {
            files.AddRange(await _dbContext.MediaFiles
                .AsNoTracking()
                .Include(file => file.Streams)
                .AsSplitQuery()
                .Where(file => batch.Contains(file.MediaItemId))
                .ToListAsync(cancellationToken));
        }

        // In configuration order, like the versions of an item in the API
        var instances = _arrOptions.Value.Instances
            .Select((instance, order) => (instance, order))
            .ToDictionary(entry => entry.instance.Key);
        return files
            .GroupBy(file => file.MediaItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<MediaVersion>)group
                    .OrderBy(file => instances.TryGetValue(file.InstanceKey, out var entry) ? entry.order : int.MaxValue)
                    .Select(file => Version(file, instances.TryGetValue(file.InstanceKey, out var entry) ? entry.instance.DisplayName : file.InstanceKey))
                    .ToList());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryItem>> ContinueWatchingAsync(Caller caller, int limit, CancellationToken cancellationToken)
    {
        var items = await _library.ContinueWatchingAsync(caller.UserId, Math.Clamp(limit, 1, MaxPage), cancellationToken);
        return items.Select(ToItem).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryItem>> NextUpAsync(Caller caller, int limit, CancellationToken cancellationToken)
    {
        var items = await _library.NextUpAsync(caller.UserId, Math.Clamp(limit, 1, MaxPage), cancellationToken);
        return items.Select(ToItem).ToList();
    }

    /// <inheritdoc />
    public async Task<ArtworkData?> ArtworkAsync(Guid itemId, ArtworkKind kind, ArtworkSize size, CancellationToken cancellationToken)
    {
        try
        {
            var image = await _images.GetAsync(itemId, ToImageKind(kind), ToImageSize(size), cancellationToken);
            if (image is null)
            {
                return null;
            }

            return new ArtworkData(image.Content, image.ContentType);
        }
        catch (UpstreamException)
        {
            // Radarr or Sonarr away: no image is what a client can show
            return null;
        }
    }

    private static MediaVersion Version(MediaFile file, string instanceName)
    {
        var streams = file.Streams.OrderBy(stream => stream.SortOrder).ToList();
        var video = streams.FirstOrDefault(stream => stream.Kind == StreamKind.Video);
        var audio = streams.Where(stream => stream.Kind == StreamKind.Audio).Select(Track).ToList();
        var subtitles = streams.Where(stream => stream.Kind == StreamKind.Subtitle).Select(Track).ToList();

        return new MediaVersion(file.Id, instanceName, Path.GetFileName(file.Path), file.SizeBytes, file.Container, file.DurationMs,
            video?.Codec, video?.Width, video?.Height, Formats.Hdr(video?.HdrFormat), audio, subtitles,
            video?.Profile, video?.BitDepth, video?.FrameRate, video?.IndexHint)
        {
            // What the file route answers with, so a bridge that announces a type announces the one that is sent
            ContentType = FileService.VideoContentType(file.Container)
        };
    }

    // Order is the place of the track in the container, which the API only hands out inside a playback plan
    private static MediaTrack Track(MediaStream stream) =>
        new(stream.Id, stream.Language, stream.Codec, stream.Channels, stream.Title, stream.IsDefault, stream.IsForced, stream.IsHearingImpaired,
            stream.IsExternal, stream.IsTextBased, stream.IndexHint, stream.Profile, stream.ChannelLayout, stream.IsCommentary);

    private static UserData? Data(UserState? state) =>
        state is null ? null : new UserData(state.PositionMs, state.IsPlayed, state.PlayCount, state.LastPlayedAt);

    private static LibraryItem ToItem(ItemSummary item) =>
        new(item.Id, Kind(item.Type), item.Title, item.Year, item.Overview, item.ExternalIds.Imdb, item.ExternalIds.Tmdb, item.ExternalIds.Tvdb,
            item.SeriesId, item.SeasonNumber, item.EpisodeNumber, item.AirDate, item.RuntimeMinutes, item.AddedAt, Artwork(item.Images),
            item.SeriesTitle, item.ParentId, Data(item.UserState), item.UnplayedCount);

    private static LibraryItem ToItem(ItemDetail item) =>
        new(item.Id, Kind(item.Type), item.Title, item.Year, item.Overview, item.ExternalIds.Imdb, item.ExternalIds.Tmdb, item.ExternalIds.Tvdb,
            item.SeriesId, item.SeasonNumber, item.EpisodeNumber, item.AirDate, item.RuntimeMinutes, item.AddedAt, Artwork(item.Images),
            item.SeriesTitle, item.ParentId, Data(item.UserState), item.UnplayedCount);

    private static ItemKind Kind(MediaItemType type) => type switch
    {
        MediaItemType.Series => ItemKind.Series,
        MediaItemType.Season => ItemKind.Season,
        MediaItemType.Episode => ItemKind.Episode,
        _ => ItemKind.Movie
    };

    private static IReadOnlyList<ArtworkKind> Artwork(IReadOnlyList<ItemImage> images) =>
        images.Select(image => image.Kind switch
            {
                ImageKind.Fanart => ArtworkKind.Backdrop,
                ImageKind.Banner => ArtworkKind.Banner,
                ImageKind.Logo => ArtworkKind.Logo,
                ImageKind.Still => ArtworkKind.Still,
                _ => ArtworkKind.Poster
            })
            .Distinct()
            .ToList();

    public static ImageKind ToImageKind(ArtworkKind kind) => kind switch
    {
        ArtworkKind.Backdrop => ImageKind.Fanart,
        ArtworkKind.Banner => ImageKind.Banner,
        ArtworkKind.Logo => ImageKind.Logo,
        ArtworkKind.Still => ImageKind.Still,
        _ => ImageKind.Poster
    };

    public static ImageSize ToImageSize(ArtworkSize size) => size switch
    {
        ArtworkSize.Medium => ImageSize.Medium,
        ArtworkSize.Small => ImageSize.Small,
        _ => ImageSize.Original
    };
}
