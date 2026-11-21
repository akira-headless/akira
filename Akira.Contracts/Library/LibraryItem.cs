namespace Akira.Contracts.Library;

/// <summary>
/// Represents a movie, series, season or episode with something to play: a movie or episode with a file, or a series or
/// season with an episode that has one.
/// </summary>
public sealed record LibraryItem(
    Guid Id,
    ItemKind Kind,
    string Title,
    int? Year,
    string? Overview,
    string? ImdbId,
    int? TmdbId,
    int? TvdbId,
    Guid? SeriesId,
    int? SeasonNumber,
    int? EpisodeNumber,
    DateOnly? AirDate,
    int? RuntimeMinutes,
    DateTimeOffset? AddedAt,
    IReadOnlyList<ArtworkKind> Artwork,
    string? SeriesTitle = null,
    Guid? ParentId = null,
    UserData? UserData = null,
    int? UnplayedCount = null);
