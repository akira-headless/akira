namespace Akira.Server.Models.Plugins;

/// <summary>
/// Represents a DLL in the plugin folder that did not load, with why.
/// </summary>
public sealed record SkippedPluginModel(string File, string Reason);
