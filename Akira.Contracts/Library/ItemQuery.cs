namespace Akira.Contracts.Library;

/// <summary>
/// Represents a request for one page of movies or series. Kind is Movie or Series, Limit is 1 to 200, Reverse turns the
/// order around (oldest first, Z to A) and Unplayed leaves out what the caller finished. Letter keeps the titles filed
/// under one letter, A to Z, or # for those that start with a digit or a symbol, in title order. Prefix keeps the titles
/// whose sort title starts with it, compared as search compares (case and accents ignored).
/// </summary>
public sealed record ItemQuery(ItemKind Kind)
{
    public string? Search { get; init; }
    public ItemOrder Order { get; init; } = ItemOrder.RecentlyAdded;
    public bool Reverse { get; init; }
    public bool Unplayed { get; init; }
    public string? Letter { get; init; }
    public string? Prefix { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 100;
}
