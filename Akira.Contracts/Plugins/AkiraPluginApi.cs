namespace Akira.Contracts.Plugins;

/// <summary>
/// Defines the version of this contract. A plugin built against another major version, or a newer minor, is not loaded.
/// A minor only adds: members to what the host implements, init properties to records, default methods to what a
/// plugin implements. Anything else raises the major.
/// </summary>
public static class AkiraPluginApi
{
    public const int Major = 1;
    public const int Minor = 0;
}
