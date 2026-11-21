using Akira.Infrastructure.Integrations.Arr;

namespace Akira.Infrastructure.Integrations.Sonarr;

/// <summary>
/// Represents a series as Sonarr's API returns it.
/// </summary>
public sealed record SonarrSeries(
    int Id,
    string? Title,
    string? SortTitle,
    int? Year,
    string? Overview,
    int? Runtime,
    int? TvdbId,
    int? TmdbId,
    string? ImdbId,
    ArrLanguage? OriginalLanguage,
    DateTimeOffset? Added,
    List<SonarrSeason>? Seasons,
    List<ArrImage>? Images,
    Radarr.ArrRating? Ratings = null,
    List<string>? Genres = null,
    string? Certification = null);
