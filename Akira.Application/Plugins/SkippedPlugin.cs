namespace Akira.Server.Plugins;

/// <summary>
/// Represents a plugin DLL that did not load, with the reason why.
/// </summary>
public sealed record SkippedPlugin(string File, string Reason);
