namespace Akira.Server.Models.Plugins;

/// <summary>
/// Represents the plugins of the server. Path is the folder plugins are loaded from, and Skipped are
/// DLLs in it that did not load, with why.
/// </summary>
public sealed record PluginOverview(string Path, IReadOnlyList<PluginSummary> Plugins, IReadOnlyList<SkippedPluginModel> Skipped);
