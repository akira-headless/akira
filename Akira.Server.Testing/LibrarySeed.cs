using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Arr;

namespace Akira.Server.Testing;

/// <summary>
/// Writes a small library across two Radarr instances and one Sonarr, with available and missing items.
/// </summary>
public static class LibrarySeed
{
    public static readonly ArrInstanceOptions Radarr = Instance("radarr", "Radarr", ArrKind.Radarr);
    public static readonly ArrInstanceOptions Radarr4K = Instance("radarr-4k", "Radarr 4K", ArrKind.Radarr);
    public static readonly ArrInstanceOptions Sonarr = Instance("sonarr", "Sonarr", ArrKind.Sonarr);
    public static readonly List<ArrInstanceOptions> Instances = [Radarr, Radarr4K, Sonarr];

    public sealed record Ids(
        Guid Heat,
        Guid Alien,
        Guid Wolf,
        Guid Series,
        Guid Season1,
        Guid Season2,
        Guid Episode1,
        Guid Episode2,
        Guid MissingSeries,
        Guid Heat4KFile);

    public static async Task<Ids> SeedAsync(AkiraDbContext dbContext, CancellationToken cancellationToken)
    {
        // Heat is in both Radarrs: one item, two versions, one poster from each
        var heat = Movie(dbContext, "Heat", 1995, 949, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        heat.Overview = "A group of professional bank robbers.";
        Link(dbContext, heat, Radarr4K.Key, 11);
        AddFile(dbContext, heat, Radarr.Key, "/data/movies/Heat (1995)/Heat.mkv", 8_000_000_000);
        var heat4K = AddFile(dbContext, heat, Radarr4K.Key, "/data/movies-4k/Heat (1995)/Heat.mkv", 60_000_000_000);
        Probe(dbContext, heat4K);
        AddImage(dbContext, heat, Radarr.Key, ImageKind.Poster, 949, "poster.jpg", "https://image.tmdb.org/t/p/original/heat.jpg");
        AddImage(dbContext, heat, Radarr4K.Key, ImageKind.Poster, 11, "poster.jpg", null);

        var alien = Movie(dbContext, "Alien", 1979, 348, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var wolf = Movie(dbContext, "100% Wolf", 2020, 1000, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        AddFile(dbContext, wolf, Radarr.Key, "/data/movies/100% Wolf (2020)/100% Wolf.mkv", 2_000_000_000);

        var series = Item(dbContext, MediaItemType.Series, "Mayfair Witches", "series:tvdb:1", null, 1);
        series.Year = 2023;
        series.OriginalLanguage = "eng";
        var season1 = Item(dbContext, MediaItemType.Season, "Season 1", "series:tvdb:1:s1", series, null);
        season1.SeasonNumber = 1;
        var season2 = Item(dbContext, MediaItemType.Season, "Season 2", "series:tvdb:1:s2", series, null);
        season2.SeasonNumber = 2;
        var episode1 = Episode(dbContext, "The Witching Hour", "series:tvdb:1:s1:e1", season1, 1, 1, 101);
        AddFile(dbContext, episode1, Sonarr.Key, "/data/tv/Mayfair Witches/S01E01.mkv", 1_000_000_000);
        AddImage(dbContext, episode1, Sonarr.Key, ImageKind.Still, null, null, "https://artworks.thetvdb.com/banners/episode/1.jpg");
        var episode2 = Episode(dbContext, "Second Sight", "series:tvdb:1:s1:e2", season1, 1, 2, 102);
        Episode(dbContext, "Third Time", "series:tvdb:1:s2:e1", season2, 2, 1, 103);

        var missingSeries = Item(dbContext, MediaItemType.Series, "Missing Show", "series:tvdb:2", null, 2);
        var missingSeason = Item(dbContext, MediaItemType.Season, "Season 1", "series:tvdb:2:s1", missingSeries, null);
        Episode(dbContext, "Pilot", "series:tvdb:2:s1:e1", missingSeason, 1, 1, 201);

        await dbContext.SaveChangesAsync(cancellationToken);
        return new Ids(heat.Id, alien.Id, wolf.Id, series.Id, season1.Id, season2.Id, episode1.Id, episode2.Id, missingSeries.Id,
            heat4K.Id);
    }

    private static MediaItem Movie(AkiraDbContext dbContext, string title, int year, int tmdbId, DateTimeOffset addedAt)
    {
        var movie = new MediaItem
        {
            Type = MediaItemType.Movie,
            IdentityKey = $"movie:tmdb:{tmdbId}",
            TmdbId = tmdbId,
            Title = title,
            SortTitle = title.ToLowerInvariant(),
            Year = year,
            AddedAt = addedAt
        };
        dbContext.MediaItems.Add(movie);
        Link(dbContext, movie, Radarr.Key, tmdbId);
        return movie;
    }

    private static MediaItem Episode(
        AkiraDbContext dbContext,
        string title,
        string key,
        MediaItem season,
        int seasonNumber,
        int episodeNumber,
        int sonarrId)
    {
        var episode = Item(dbContext, MediaItemType.Episode, title, key, season, sonarrId);
        episode.SeasonNumber = seasonNumber;
        episode.EpisodeNumber = episodeNumber;
        episode.AirDate = new DateOnly(2023, 1, episodeNumber);
        return episode;
    }

    private static MediaItem Item(AkiraDbContext dbContext, MediaItemType type, string title, string key, MediaItem? parent, int? sonarrId)
    {
        var item = new MediaItem
        {
            Type = type,
            IdentityKey = key,
            Title = title,
            SortTitle = title.ToLowerInvariant(),
            Parent = parent,
            ParentId = parent?.Id
        };
        dbContext.MediaItems.Add(item);
        Link(dbContext, item, Sonarr.Key, sonarrId);
        return item;
    }

    private static void Link(AkiraDbContext dbContext, MediaItem item, string instanceKey, int? arrId) =>
        dbContext.ArrLinks.Add(new ArrLink { MediaItemId = item.Id, InstanceKey = instanceKey, ArrId = arrId });

    private static MediaFile AddFile(AkiraDbContext dbContext, MediaItem item, string instanceKey, string path, long size)
    {
        var file = new MediaFile
        {
            MediaItemId = item.Id,
            InstanceKey = instanceKey,
            Path = path,
            SizeBytes = size,
            Container = "mkv",
            ArrFileId = 1,
            AddedAt = item.AddedAt
        };
        dbContext.MediaFiles.Add(file);
        return file;
    }

    // A 4K HDR10 remux with two audio tracks, a forced and a full subtitle and two chapters
    private static void Probe(AkiraDbContext dbContext, MediaFile file)
    {
        file.ProbeStatus = ProbeStatus.Probed;
        file.ProbeVersion = 1;
        file.ProbedAt = DateTimeOffset.UtcNow;
        file.DurationMs = 10_200_000;

        MediaStream Stream(string id, StreamKind kind, int order, string? language, string codec) => new()
        {
            Id = id,
            MediaFileId = file.Id,
            Kind = kind,
            SortOrder = order,
            Language = language,
            Codec = codec,
            IndexHint = order
        };

        var video = Stream("a0000000000000000000000000000000", StreamKind.Video, 0, null, "hevc");
        (video.Width, video.Height, video.HdrFormat, video.BitDepth) = (3840, 2160, HdrFormat.Hdr10, 10);
        var english = Stream("a1000000000000000000000000000000", StreamKind.Audio, 1, "eng", "truehd");
        (english.Channels, english.ChannelLayout, english.IsDefault) = (8, "7.1", true);
        var commentary = Stream("a2000000000000000000000000000000", StreamKind.Audio, 2, "eng", "ac3");
        (commentary.Channels, commentary.IsCommentary, commentary.Title) = (2, true, "Commentary");
        var forced = Stream("a3000000000000000000000000000000", StreamKind.Subtitle, 3, "eng", "hdmv_pgs_subtitle");
        (forced.IsForced, forced.EventCount) = (true, 24);
        var full = Stream("a4000000000000000000000000000000", StreamKind.Subtitle, 1000, "dut", "subrip");
        (full.IsExternal, full.IsTextBased, full.EventCount, full.ExternalPath) = (true, true, 1500, "/data/movies-4k/Heat (1995)/Heat.nl.srt");

        dbContext.MediaStreams.AddRange(video, english, commentary, forced, full);
        dbContext.MediaChapters.AddRange(
            new MediaChapter { MediaFileId = file.Id, SortOrder = 0, Title = "Opening", StartMs = 0, EndMs = 300_000 },
            new MediaChapter { MediaFileId = file.Id, SortOrder = 1, Title = "The Heist", StartMs = 300_000 });
    }

    private static void AddImage(
        AkiraDbContext dbContext,
        MediaItem item,
        string instanceKey,
        ImageKind kind,
        int? arrId,
        string? fileName,
        string? remoteUrl) =>
        dbContext.MediaImages.Add(new MediaImage
        {
            MediaItemId = item.Id,
            InstanceKey = instanceKey,
            Kind = kind,
            ArrId = arrId,
            ArrFileName = fileName,
            RemoteUrl = remoteUrl
        });

    private static ArrInstanceOptions Instance(string key, string name, ArrKind kind) => new()
    {
        Key = key,
        Name = name,
        Kind = kind,
        Url = $"http://{key}:7878",
        ApiKey = "key"
    };
}
