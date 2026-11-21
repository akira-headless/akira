using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents a show in Trakt's watched shows, with its watched seasons.
/// </summary>
public sealed record TraktWatchedShow(
    [property: JsonPropertyName("show")] TraktMedia Show,
    [property: JsonPropertyName("seasons")] IReadOnlyList<TraktWatchedSeason>? Seasons);
