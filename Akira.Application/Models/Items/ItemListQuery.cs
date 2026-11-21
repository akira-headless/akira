using Akira.Core.Enum;

namespace Akira.Server.Models.Items;

/// <summary>
/// Represents the filters, sort and page of a list of items. A null Type lists movies and series. TitlePrefix keeps the
/// titles whose folded sort title starts with it.
/// </summary>
public sealed record ItemListQuery(
    MediaItemType? Type,
    string? Search,
    ItemSort Sort,
    SortOrder Order,
    bool IncludeUnavailable,
    int Offset,
    int Limit,
    bool Unwatched = false,
    string? TitlePrefix = null);
