namespace Akira.Server.Models.Items;

/// <summary>
/// Represents the TMDB, TVDB and IMDb ids of an item.
/// </summary>
public sealed record ExternalIds(int? Tmdb, int? Tvdb, string? Imdb);
