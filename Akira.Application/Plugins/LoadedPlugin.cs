using System.Reflection;
using Akira.Contracts.Plugins;

namespace Akira.Server.Plugins;

/// <summary>
/// Represents a plugin the loader registered. File is null for a built-in plugin, and Provides lists the kinds of
/// service it registered, such as bridge.
/// </summary>
public sealed record LoadedPlugin(
    string Key,
    PluginManifest Manifest,
    string? Version,
    string? File,
    IPluginSettings Settings,
    IReadOnlyList<string> Provides,
    Assembly Assembly)
{
    public const string BridgeKind = "bridge";

    public bool IsConfigured => Manifest.Settings.Where(s => s.Required).All(s => Settings.Get(s.Key) is not null);

    public bool IsBridge => Provides.Contains(BridgeKind);

    // A bridge only answers once it is switched on
    public bool IsActive => IsConfigured && (!IsBridge || Settings.Enabled);
}
