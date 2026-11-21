using Akira.Contracts.Plugins;
using Akira.Contracts.Subtitles;
using Microsoft.Extensions.DependencyInjection;
using Akira.Infrastructure.Integrations.Lingarr;
using Akira.Infrastructure.Subtitles;

namespace Akira.Infrastructure.Integrations;

// Built in, so their settings stay under Subtitles: and are bound and validated as SubtitleOptions
[AkiraPlugin("lingarr")]
public sealed class LingarrPlugin : IAkiraPlugin
{
    public const string Section = $"{SubtitleOptions.SectionName}:{nameof(SubtitleOptions.Lingarr)}";

    public PluginManifest Manifest { get; } = new("Lingarr", "Translates subtitles with Lingarr, which reads and writes them on the same share.",
    [
        new PluginSetting(nameof(LingarrOptions.Url), "Url", PluginSettingKind.Url) { Required = true },
        new PluginSetting(nameof(LingarrOptions.ApiKey), "API key", PluginSettingKind.Secret) { Description = "Only when Lingarr has authentication on." },
    ]);

    public void ConfigureServices(IServiceCollection services, IPluginSettings settings)
    {
        services.AddSubtitleHttpClient(LingarrClient.HttpClientName);
        services.AddSingleton<ISubtitleTranslator, LingarrClient>();
    }
}
