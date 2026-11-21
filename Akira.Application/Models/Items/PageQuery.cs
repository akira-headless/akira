namespace Akira.Server.Models.Items;

/// <summary>
/// Represents the page of the children of an item to list, and whether unavailable ones are in it.
/// </summary>
public sealed record PageQuery(bool IncludeUnavailable, int Offset, int Limit);
