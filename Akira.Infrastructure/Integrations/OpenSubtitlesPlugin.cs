using Akira.Contracts.Plugins;
using Akira.Contracts.Subtitles;
using Microsoft.Extensions.DependencyInjection;
using Akira.Infrastructure.Integrations.OpenSubtitles;
using Akira.Infrastructure.Subtitles;

namespace Akira.Infrastructure.Integrations;

// Built in, so their settings stay under Subtitles: and are bound and validated as SubtitleOptions
[AkiraPlugin("opensubtitles")]
public sealed class OpenSubtitlesPlugin : IAkiraPlugin
{
    public const string Section = $"{SubtitleOptions.SectionName}:{nameof(SubtitleOptions.OpenSubtitles)}";

    public PluginManifest Manifest { get; } = new("OpenSubtitles.com", "Searches and downloads subtitles with the household's own API key.",
    [
        new PluginSetting(nameof(OpenSubtitlesOptions.ApiKey), "API key", PluginSettingKind.Secret) { Required = true },
        new PluginSetting(nameof(OpenSubtitlesOptions.Username), "Username", PluginSettingKind.Text) { Description = "With the password, for more downloads a day." },
        new PluginSetting(nameof(OpenSubtitlesOptions.Password), "Password", PluginSettingKind.Secret),
        new PluginSetting(nameof(OpenSubtitlesOptions.Url), "Url", PluginSettingKind.Url)
    ]);

    public void ConfigureServices(IServiceCollection services, IPluginSettings settings)
    {
        services.AddSubtitleHttpClient(OpenSubtitlesClient.HttpClientName);
        services.AddSingleton<ISubtitleSource, OpenSubtitlesClient>();
    }
}
