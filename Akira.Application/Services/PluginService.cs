using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Plugins;
using Akira.Server.Plugins;

namespace Akira.Server.Services;

public sealed class PluginService : IPluginService
{
    private readonly PluginCatalog _catalog;

    public PluginService(PluginCatalog catalog)
    {
        _catalog = catalog;
    }

    /// <inheritdoc />
    public PluginOverview Overview()
    {
        var plugins = _catalog.Plugins.Select(ToSummary).ToList();
        var skippedPlugins = _catalog.Skipped.Select(skipped => new SkippedPluginModel(skipped.File, skipped.Reason)).ToList();
        return new PluginOverview(_catalog.Path, plugins, skippedPlugins);
    }

    private static PluginSummary ToSummary(LoadedPlugin plugin)
    {
        var settings = plugin.Manifest.Settings
            .Select(setting => new PluginSettingModel(setting.Key, setting.Label, setting.Kind, setting.Required,
                plugin.Settings.Get(setting.Key) is not null, setting.Description))
            .ToList();
        return new PluginSummary(
            plugin.Key,
            plugin.Manifest.Name,
            plugin.Manifest.Description,
            plugin.Version,
            plugin.File ?? "built-in",
            plugin.Provides,
            StatusOf(plugin),
            settings);
    }

    private static PluginStatus StatusOf(LoadedPlugin plugin)
    {
        if (plugin.IsActive)
        {
            return PluginStatus.Active;
        }

        return plugin.IsConfigured ? PluginStatus.Disabled : PluginStatus.NotConfigured;
    }
}
