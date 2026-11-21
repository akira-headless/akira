namespace Akira.Contracts.Plugins;

/// <summary>
/// Marks the entry class of a plugin. The key is lowercase letters, digits and dashes, at most 32.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AkiraPluginAttribute : Attribute
{
    public string Key { get; }

    public AkiraPluginAttribute(string key)
    {
        Key = key;
    }
}
