namespace Akira.Server.Models.Items;

/// <summary>
/// Represents an instance that holds an item, which a delete or unmonitor names. A series has no versions of its own.
/// </summary>
public sealed record ItemInstance(string Key, string Name);
