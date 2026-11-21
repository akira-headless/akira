namespace Akira.Contracts.Plugins;

/// <summary>
/// Represents a plugin's name, a short description and the settings it reads.
/// </summary>
public sealed record PluginManifest(string Name, string Description, IReadOnlyList<PluginSetting> Settings);
