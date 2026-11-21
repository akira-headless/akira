namespace Akira.Core.Library;

/// <summary>
/// Represents a path prefix on one system and where the same folder is on another.
/// </summary>
public sealed record PathMapping(string From, string To);
