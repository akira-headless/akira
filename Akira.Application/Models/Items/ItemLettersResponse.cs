namespace Akira.Server.Models.Items;

/// <summary>
/// Represents the first letters of the titles, for jumping in the A to Z list. Total is the count of
/// all titles that match.
/// </summary>
public sealed record ItemLettersResponse(IReadOnlyList<ItemLetter> Letters, int Total);
