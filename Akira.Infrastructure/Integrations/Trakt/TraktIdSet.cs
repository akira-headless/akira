using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents the ids Trakt gives a movie or show, of which only TMDB and TVDB are read.
/// </summary>
public sealed record TraktIdSet(
    [property: JsonPropertyName("tmdb")] int? Tmdb,
    [property: JsonPropertyName("tvdb")] int? Tvdb);
