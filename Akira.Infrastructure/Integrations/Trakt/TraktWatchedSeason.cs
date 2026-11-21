using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents a season in Trakt's watched shows, by number, with its watched episodes.
/// </summary>
public sealed record TraktWatchedSeason(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("episodes")] IReadOnlyList<TraktWatchedEpisode> Episodes);
