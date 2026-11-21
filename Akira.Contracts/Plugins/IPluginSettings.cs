namespace Akira.Contracts.Plugins;

/// <summary>
/// Reads the plugin's own settings. Values come from configuration, so they are fixed for the server's lifetime.
/// </summary>
public interface IPluginSettings
{
    string Section { get; }

    bool Enabled { get; }

    /// <summary>
    /// Reads a setting under the plugin's section, or null when it is empty or not set.
    /// </summary>
    string? Get(string key);
}
