namespace Akira.Server.Models.Items;

/// <summary>
/// Represents a first letter of the titles. Offset is where its first title is in the A to Z list,
/// Count how many it has.
/// </summary>
public sealed record ItemLetter(string Letter, int Offset, int Count);
