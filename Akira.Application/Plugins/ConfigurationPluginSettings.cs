using Akira.Contracts.Plugins;

namespace Akira.Server.Plugins;

internal sealed class ConfigurationPluginSettings : IPluginSettings
{
    private readonly IConfigurationSection _section;
    private readonly string _path;

    public ConfigurationPluginSettings(
        IConfigurationSection section,
        string path)
    {
        _section = section;
        _path = path;
    }

    public string Section => _path;

    public bool Enabled => bool.TryParse(_section["Enabled"], out var enabled) && enabled;

    /// <inheritdoc />
    public string? Get(string key)
    {
        var value = _section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }
}
