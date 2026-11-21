namespace Akira.Contracts.Subtitles;

/// <summary>
/// Represents a search for subtitles in one language. Language is ISO 639-2/B, and the series ids are only set for an
/// episode.
/// </summary>
public sealed record SubtitleSearch(
    string Language,
    MediaKind Type,
    int? TmdbId,
    string? ImdbId,
    int? SeriesTmdbId,
    string? SeriesImdbId,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? FileHash);
