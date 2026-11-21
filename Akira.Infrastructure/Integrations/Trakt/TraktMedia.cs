using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents a movie or show in a Trakt answer, by its ids.
/// </summary>
public sealed record TraktMedia([property: JsonPropertyName("ids")] TraktIdSet Ids);
