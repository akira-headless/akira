using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;

namespace Akira.Server.Testing.Playback;

/// <summary>
/// Writes a movie with two versions and a three episode series, with probed tracks, for the playback tests.
/// </summary>
public static class PlaybackSeed
{
    public const long MovieDurationMs = 6_000_000;
    public const long EpisodeDurationMs = 2_700_000;

    public sealed record Ids(Guid Heat, Guid HeatFullHd, Guid HeatUhd, Guid Alien, Guid Series, Guid Episode1, Guid Episode2, Guid Episode3);

    public static async Task<Ids> SeedAsync(AkiraDbContext dbContext, string mediaRoot, CancellationToken cancellationToken)
    {
        var heat = Item(dbContext, MediaItemType.Movie, "Heat", "movie:tmdb:949", null, "radarr", "radarr-4k");
        heat.OriginalLanguage = "eng";

        var fullHd = File(dbContext, heat, "radarr", Path.Combine(mediaRoot, "movies", "Heat (1995)", "Heat.mkv"), MovieDurationMs);
        Video(dbContext, fullHd, "h264", 1080, HdrFormat.Sdr);
        Audio(dbContext, fullHd, "fhd-ac3", 1, "eng", "ac3", 6, isDefault: true);
        Audio(dbContext, fullHd, "fhd-commentary", 2, "eng", "aac", 2, commentary: true, title: "Commentary");
        MovieSubtitles(dbContext, fullHd, "fhd", Path.Combine(mediaRoot, "movies", "Heat (1995)", "Heat.nl.srt"));

        var uhd = File(dbContext, heat, "radarr-4k", Path.Combine(mediaRoot, "movies-4k", "Heat (1995)", "Heat.mkv"), MovieDurationMs);
        Video(dbContext, uhd, "hevc", 2160, HdrFormat.Hdr10);
        Audio(dbContext, uhd, "uhd-truehd", 1, "eng", "truehd", 8, isDefault: true);
        Audio(dbContext, uhd, "uhd-ac3", 2, "eng", "ac3", 6);
        MovieSubtitles(dbContext, uhd, "uhd", Path.Combine(mediaRoot, "movies-4k", "Heat (1995)", "Heat.nl.srt"));
        dbContext.MediaChapters.Add(new MediaChapter { MediaFileId = uhd.Id, SortOrder = 0, Title = "Opening", StartMs = 0 });

        var alien = Item(dbContext, MediaItemType.Movie, "Alien", "movie:tmdb:348", null, "radarr");

        var series = Item(dbContext, MediaItemType.Series, "Mayfair Witches", "series:tvdb:1", null, "sonarr");
        series.OriginalLanguage = "eng";
        var season = Item(dbContext, MediaItemType.Season, "Season 1", "series:tvdb:1:s1", series, "sonarr");
        season.SeasonNumber = 1;

        var episodes = new List<MediaItem>();
        for (var number = 1; number <= 3; number++)
        {
            var episode = Item(dbContext, MediaItemType.Episode, $"Episode {number}", $"series:tvdb:1:s1:e{number}", season, "sonarr");
            (episode.SeasonNumber, episode.EpisodeNumber) = (1, number);
            var file = File(dbContext, episode, "sonarr", Path.Combine(mediaRoot, "tv", "Mayfair Witches", $"S01E0{number}.mkv"), EpisodeDurationMs);
            var prefix = $"e{number}";
            Video(dbContext, file, "h264", 1080, HdrFormat.Sdr);
            Audio(dbContext, file, $"{prefix}-eng", 1, "eng", "aac", 2, isDefault: true);
            Audio(dbContext, file, $"{prefix}-ger", 2, "ger", "ac3", 6);
            Subtitle(dbContext, file, $"{prefix}-full", 3, "eng", "subrip", 500, title: "Full");
            Subtitle(dbContext, file, $"{prefix}-signs", 4, "eng", "subrip", 20, title: "Signs");
            Subtitle(dbContext, file, $"{prefix}-ger-sub", 5, "ger", "subrip", 480);
            episodes.Add(episode);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new Ids(heat.Id, fullHd.Id, uhd.Id, alien.Id, series.Id, episodes[0].Id, episodes[1].Id, episodes[2].Id);
    }

    private static void MovieSubtitles(AkiraDbContext dbContext, MediaFile file, string prefix, string dutchPath)
    {
        Subtitle(dbContext, file, $"{prefix}-forced", 10, "eng", "hdmv_pgs_subtitle", 30, forced: true);
        Subtitle(dbContext, file, $"{prefix}-eng", 11, "eng", "hdmv_pgs_subtitle", 1800);
        var dutch = Subtitle(dbContext, file, $"{prefix}-dut", 1000, "dut", "subrip", 1500);
        (dutch.IsExternal, dutch.ExternalPath, dutch.IndexHint) = (true, dutchPath, null);
    }

    private static MediaItem Item(AkiraDbContext dbContext, MediaItemType type, string title, string key, MediaItem? parent, params string[] instances)
    {
        var item = new MediaItem
        {
            Type = type, IdentityKey = key, Title = title, SortTitle = title.ToLowerInvariant(), Parent = parent, ParentId = parent?.Id
        };
        dbContext.MediaItems.Add(item);
        foreach (var instance in instances)
        {
            dbContext.ArrLinks.Add(new ArrLink { MediaItemId = item.Id, InstanceKey = instance, ArrId = 1 });
        }

        return item;
    }

    private static MediaFile File(AkiraDbContext dbContext, MediaItem item, string instance, string path, long durationMs)
    {
        var file = new MediaFile
        {
            MediaItemId = item.Id, InstanceKey = instance, Path = path, SizeBytes = 1000, Container = "mkv", ArrFileId = 1,
            DurationMs = durationMs, ProbeStatus = ProbeStatus.Probed, ProbeVersion = 1
        };
        dbContext.MediaFiles.Add(file);
        return file;
    }

    private static void Video(AkiraDbContext dbContext, MediaFile file, string codec, int height, HdrFormat hdr) =>
        dbContext.MediaStreams.Add(new MediaStream
        {
            Id = $"{file.Id:N}", MediaFileId = file.Id, Kind = StreamKind.Video, SortOrder = 0, Codec = codec,
            Height = height, Width = height * 16 / 9, HdrFormat = hdr, IndexHint = 0
        });

    private static void Audio(AkiraDbContext dbContext, MediaFile file, string id, int order, string language, string codec, int channels,
        bool isDefault = false, bool commentary = false, string? title = null) =>
        dbContext.MediaStreams.Add(new MediaStream
        {
            Id = id, MediaFileId = file.Id, Kind = StreamKind.Audio, SortOrder = order, Language = language, Codec = codec,
            Channels = channels, IsDefault = isDefault, IsCommentary = commentary, Title = title, IndexHint = order
        });

    private static MediaStream Subtitle(AkiraDbContext dbContext, MediaFile file, string id, int order, string language, string codec,
        int events, bool forced = false, string? title = null)
    {
        var stream = new MediaStream
        {
            Id = id, MediaFileId = file.Id, Kind = StreamKind.Subtitle, SortOrder = order, Language = language, Codec = codec,
            EventCount = events, IsForced = forced, Title = title, IsTextBased = codec == "subrip", IndexHint = order
        };
        dbContext.MediaStreams.Add(stream);
        return stream;
    }
}
