using Akira.Core.Enum;
using Akira.Server.Models.Playback;

namespace Akira.Server.Models.Items;

/// <summary>
/// Represents a movie, series, season or episode as a list shows it, without its versions.
/// </summary>
public sealed record ItemSummary(
    Guid Id,
    MediaItemType Type,
    string Title,
    string SortTitle,
    int? Year,
    string? Overview,
    ExternalIds ExternalIds,
    Guid? ParentId,
    Guid? SeriesId,
    string? SeriesTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    DateOnly? AirDate,
    int? RuntimeMinutes,
    DateTimeOffset? AddedAt,
    bool IsAvailable,
    IReadOnlyList<ItemImage> Images,
    UserState? UserState,
    int? UnplayedCount,
    // Scores out of 10 for a card: IMDb for a movie (from Radarr), the series rating from Sonarr
    double? ImdbRating,
    double? Rating);
