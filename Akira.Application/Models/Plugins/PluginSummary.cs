namespace Akira.Server.Models.Plugins;

/// <summary>
/// Represents a loaded plugin. Source is "built-in" or the DLL's file name, and Provides lists what it
/// registered, such as subtitleSource.
/// </summary>
public sealed record PluginSummary(
    string Key,
    string Name,
    string Description,
    string? Version,
    string Source,
    IReadOnlyList<string> Provides,
    PluginStatus Status,
    IReadOnlyList<PluginSettingModel> Settings);
