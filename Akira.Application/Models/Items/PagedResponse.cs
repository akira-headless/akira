namespace Akira.Server.Models.Items;

/// <summary>
/// Represents one page of items. Total is the count of all items that match, not only this page.
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);
