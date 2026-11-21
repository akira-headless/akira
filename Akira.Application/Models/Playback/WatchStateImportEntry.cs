namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the watch state of one movie or episode to import. For an episode the ids are its series' ids, with the
/// season and episode number.
/// </summary>
public sealed record WatchStateImportEntry(
    WatchStateImportKind? Kind,
    int? TmdbId,
    int? TvdbId,
    string? ImdbId,
    int? SeasonNumber,
    int? EpisodeNumber,
    bool Played,
    long? PositionMs,
    int? PlayCount,
    DateTimeOffset? LastPlayedAt);
