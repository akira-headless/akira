using Akira.Core.Enum;
using Akira.Server.Models.Playback;

namespace Akira.Server.Models.Items;

/// <summary>
/// Represents a movie, series, season or episode with its versions, the instances that hold it and
/// the user's watch state.
/// </summary>
public sealed record ItemDetail(
    Guid Id,
    MediaItemType Type,
    string Title,
    string SortTitle,
    int? Year,
    string? Overview,
    Guid? ParentId,
    Guid? SeriesId,
    string? SeriesTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    DateOnly? AirDate,
    int? RuntimeMinutes,
    string? OriginalLanguage,
    ExternalIds ExternalIds,
    DateTimeOffset? AddedAt,
    bool IsAvailable,
    IReadOnlyList<ItemInstance> Instances,
    IReadOnlyList<ItemVersion> Versions,
    IReadOnlyList<ItemImage> Images,
    UserState? UserState,
    int? UnplayedCount,
    ItemInfo Info);
