using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents an episode in Trakt's watched shows, by its number in the season.
/// </summary>
public sealed record TraktWatchedEpisode(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("plays")] int Plays,
    [property: JsonPropertyName("last_watched_at")] DateTimeOffset? LastWatchedAt);
