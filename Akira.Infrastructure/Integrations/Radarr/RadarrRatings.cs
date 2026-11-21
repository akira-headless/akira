namespace Akira.Infrastructure.Integrations.Radarr;

/// <summary>
/// Represents the ratings of a movie, which Radarr keys by source. Each is a score out of 10 with its vote count.
/// </summary>
public sealed record RadarrRatings(ArrRating? Imdb, ArrRating? Tmdb);
