namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents a movie by TMDB id, or an episode by its show's TVDB id and its numbers.
/// </summary>
public sealed record TraktItem(int? Tmdb, int? Tvdb, int? Season, int? Episode)
{
    public bool IsMovie => Tmdb is not null;
}
