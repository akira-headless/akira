namespace Akira.Server.Models.Plugins;

public enum PluginStatus
{
    Active,
    NotConfigured,

    // A bridge that is set up but not switched on with Plugins:{key}:Enabled
    Disabled
}
