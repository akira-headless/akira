using Microsoft.Extensions.DependencyInjection;

namespace Akira.Contracts.Plugins;

/// <summary>
/// Defines the entry point of a plugin, created once at startup with a parameterless constructor.
/// </summary>
public interface IAkiraPlugin
{
    PluginManifest Manifest { get; }

    /// <summary>
    /// Registers what the plugin provides, such as an ISubtitleSource, also when the plugin is not configured. A
    /// provider tells through IsConfigured whether it can be used.
    /// </summary>
    void ConfigureServices(IServiceCollection services, IPluginSettings settings);
}
