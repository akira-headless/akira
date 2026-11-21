namespace Akira.Server.Plugins;

public sealed class PluginSettingsException : Exception
{
    public PluginSettingsException(string message)
        : base(message)
    {
    }
}
