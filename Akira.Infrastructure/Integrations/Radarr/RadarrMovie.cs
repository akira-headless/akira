using Akira.Infrastructure.Integrations.Arr;

namespace Akira.Infrastructure.Integrations.Radarr;

/// <summary>
/// Represents a movie as Radarr's API returns it.
/// </summary>
public sealed record RadarrMovie(
    int Id,
    string? Title,
    string? SortTitle,
    int? Year,
    string? Overview,
    int? Runtime,
    int? TmdbId,
    string? ImdbId,
    ArrLanguage? OriginalLanguage,
    DateTimeOffset? Added,
    bool HasFile,
    RadarrMovieFile? MovieFile,
    List<ArrImage>? Images,
    RadarrRatings? Ratings = null,
    string? YouTubeTrailerId = null,
    List<string>? Genres = null,
    string? Certification = null);
