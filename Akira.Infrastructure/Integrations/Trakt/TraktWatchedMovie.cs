using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents a movie in Trakt's watched movies, with its play count and when it was last watched.
/// </summary>
public sealed record TraktWatchedMovie(
    [property: JsonPropertyName("plays")] int Plays,
    [property: JsonPropertyName("last_watched_at")] DateTimeOffset? LastWatchedAt,
    [property: JsonPropertyName("movie")] TraktMedia Movie);
