namespace Akira.Contracts.Plugins;

/// <summary>
/// Defines the kinds of value a plugin setting holds. User is the name of an Akira user or profile.
/// </summary>
public enum PluginSettingKind
{
    Text,
    Secret,
    Number,
    Toggle,
    Url,
    User
}
