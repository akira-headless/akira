namespace Akira.Contracts.Plugins;

/// <summary>
/// Declares the contract version a plugin assembly was built against. Every plugin assembly has one.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AkiraPluginApiVersionAttribute : Attribute
{
    public int Major { get; }
    public int Minor { get; }

    public AkiraPluginApiVersionAttribute(int major, int minor)
    {
        Major = major;
        Minor = minor;
    }
}
