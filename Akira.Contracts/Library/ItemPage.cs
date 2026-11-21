namespace Akira.Contracts.Library;

/// <summary>
/// Represents one page of items. Total is the count of all items that match, not only this page.
/// </summary>
public sealed record ItemPage(IReadOnlyList<LibraryItem> Items, int Total);
