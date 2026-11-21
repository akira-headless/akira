using System.Reflection;
using Akira.Contracts.Bridges;

namespace Akira.Server.Plugins;

/// <summary>
/// Holds what loaded at startup, and what did not and why.
/// </summary>
public sealed class PluginCatalog
{
    private readonly Lock _making = new();
    private Dictionary<string, IClientBridge>? _bridges;
    private HashSet<Assembly> _bridgeAssemblies = [];

    public PluginCatalog(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public List<LoadedPlugin> Plugins { get; } = [];
    public List<SkippedPlugin> Skipped { get; } = [];

    public IEnumerable<LoadedPlugin> ActiveBridges => Plugins.Where(p => p.IsBridge && p.IsActive);

    /// <summary>
    /// Returns the bridge of every switched on plugin by its key, made once. A plugin whose bridge cannot be made is
    /// skipped with the reason, like a DLL that does not load, so it never stops the server.
    /// </summary>
    public IReadOnlyDictionary<string, IClientBridge> Bridges(IServiceProvider services)
    {
        lock (_making)
        {
            if (_bridges is not null)
            {
                return _bridges;
            }

            var bridges = new Dictionary<string, IClientBridge>();
            _bridgeAssemblies = ActiveBridges.Select(plugin => plugin.Assembly).ToHashSet();
            foreach (var plugin in ActiveBridges.ToList())
            {
                try
                {
                    bridges[plugin.Key] = services.GetRequiredKeyedService<IClientBridge>(plugin.Key);
                }
                catch (Exception exception)
                {
                    var skipped = new SkippedPlugin(plugin.File ?? plugin.Key, $"Its bridge could not be made: {Reason(exception)}");
                    Plugins.Remove(plugin);
                    Skipped.Add(skipped);
                    services.GetService<ILogger<PluginCatalog>>()?.LogWarning("Skipped plugin {File}: {Reason}", skipped.File, skipped.Reason);
                }
            }

            _bridges = bridges;
            return bridges;
        }
    }

    /// <summary>
    /// Returns the assemblies of the switched on bridges, also of one whose bridge could not be made, whose controllers
    /// must not answer anywhere.
    /// </summary>
    public IReadOnlySet<Assembly> BridgeAssemblies(IServiceProvider services)
    {
        Bridges(services);
        return _bridgeAssemblies;
    }

    // What the plugin's own code threw, not the wrapper reflection puts around it
    public static string Reason(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner } ? Reason(inner) : exception.Message;

    public void Log(ILogger logger)
    {
        foreach (var plugin in Plugins.Where(p => p.File is not null))
        {
            logger.LogInformation("Loaded plugin {Plugin} {Version} from {File}", plugin.Key, plugin.Version, plugin.File);
        }

        foreach (var skipped in Skipped)
        {
            logger.LogWarning("Skipped plugin {File}: {Reason}", skipped.File, skipped.Reason);
        }
    }
}
