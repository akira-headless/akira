namespace Akira.Server.Models.Items;

/// <summary>
/// Represents the ratings, genres, certification and trailer of an item. Scores are out of 10, IMDb and
/// TMDB from Radarr, Rating from Sonarr. An episode or season has its series' genres and certification.
/// </summary>
public sealed record ItemInfo(
    double? ImdbRating,
    int? ImdbVotes,
    double? TmdbRating,
    double? Rating,
    IReadOnlyList<string> Genres,
    string? Certification,
    string? TrailerUrl);
