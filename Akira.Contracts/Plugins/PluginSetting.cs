namespace Akira.Contracts.Plugins;

/// <summary>
/// Represents a setting a plugin reads. Key is the name under the plugin's section, Plugins:{key}:{Key}.
/// </summary>
public sealed record PluginSetting(string Key, string Label, PluginSettingKind Kind)
{
    public bool Required { get; init; }
    public string? Description { get; init; }
}
