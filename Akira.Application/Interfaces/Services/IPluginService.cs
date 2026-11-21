using Akira.Server.Models.Plugins;

namespace Akira.Server.Interfaces.Services;

public interface IPluginService
{
    /// <summary>
    /// Lists the plugins that loaded, built in or from Plugins:Path, and the DLLs that did not.
    /// </summary>
    PluginOverview Overview();
}
