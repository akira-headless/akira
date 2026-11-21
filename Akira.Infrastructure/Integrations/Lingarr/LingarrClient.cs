using System.Globalization;
using Akira.Contracts.Subtitles;
using Microsoft.Extensions.Options;
using Akira.Core.Media;
using Akira.Infrastructure.Subtitles;

namespace Akira.Infrastructure.Integrations.Lingarr;

/// <summary>
/// Translates subtitle files with Lingarr, which reads and writes them on the same share.
/// </summary>
public sealed class LingarrClient : ISubtitleTranslator
{
    public const string HttpClientName = "lingarr";
    private const string Upstream = "Lingarr";

    private static readonly IReadOnlySet<string> SupportedFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "srt", "ass", "ssa" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<SubtitleOptions> _options;

    public LingarrClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<SubtitleOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public string Key => "lingarr";
    public string Name => "Lingarr";
    public bool IsConfigured => Settings.IsConfigured;
    public IReadOnlySet<string> Formats => SupportedFormats;

    private LingarrOptions Settings => _options.CurrentValue.Lingarr;

    /// <inheritdoc />
    public async Task<string> StartAsync(TranslationJob job, CancellationToken cancellationToken)
    {
        var settings = Settings;
        var started = await CallAsync(httpClient => UpstreamHttp.PostJsonAsync<LingarrJob>(httpClient, Upstream, "api/translate/file", new
        {
            // Lingarr's own id of the movie or episode only names the request in its list, a request works without it
            mediaId = 0,
            subtitlePath = settings.ToLingarr().Map(job.SubtitlePath),
            sourceLanguage = LingarrLanguage(job.SourceLanguage),
            targetLanguage = LingarrLanguage(job.TargetLanguage),
            mediaType = job.Type == MediaKind.Episode ? "Episode" : "Movie",
            subtitleFormat = "." + job.Format.TrimStart('.')
        }, cancellationToken));
        return started.JobId.ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<TranslationProgress> GetAsync(string jobId, CancellationToken cancellationToken)
    {
        var request = await CallAsync(httpClient => UpstreamHttp.GetJsonAsync<LingarrRequest>(httpClient, Upstream,
            "api/translationrequest/" + Uri.EscapeDataString(jobId), cancellationToken));

        var status = request.Status switch
        {
            "Completed" => TranslationState.Completed,
            "Failed" or "Interrupted" => TranslationState.Failed,
            "Cancelled" => TranslationState.Cancelled,
            "InProgress" => TranslationState.Running,
            _ => TranslationState.Queued
        };
        string? result = null;
        if (request.TranslatedSubtitle is { Length: > 0 } path)
        {
            result = Settings.FromLingarr().Map(path);
        }

        string? error = null;
        if (status == TranslationState.Failed)
        {
            error = request.ErrorMessage ?? "Lingarr could not translate the subtitle.";
        }

        return new TranslationProgress(status, Math.Clamp(request.Progress, 0, 100), result, error);
    }

    /// <inheritdoc />
    public async Task CancelAsync(string jobId, CancellationToken cancellationToken)
    {
        // Lingarr takes the whole request back; only the id matters to it, the rest has to be present
        await CallAsync(async httpClient =>
        {
            await UpstreamHttp.SendJsonAsync(httpClient, Upstream, HttpMethod.Post, "api/translationrequest/cancel", new
            {
                id = int.Parse(jobId, CultureInfo.InvariantCulture),
                title = "",
                sourceLanguage = "",
                targetLanguage = "",
                mediaType = "Movie",
                status = "InProgress"
            }, cancellationToken);
            return true;
        });
    }

    private async Task<T> CallAsync<T>(Func<HttpClient, Task<T>> call)
    {
        using var httpClient = CreateClient();
        try
        {
            return await call(httpClient);
        }
        catch (UpstreamException exception) when (exception.Failure == UpstreamFailure.Unauthorized && string.IsNullOrWhiteSpace(Settings.ApiKey))
        {
            // "Rejected the API key" would send someone looking for a key they never set
            throw new UpstreamException(Upstream, UpstreamFailure.Unauthorized,
                $"Lingarr has authentication on, set {SubtitleOptions.SectionName}:Lingarr:ApiKey.", exception.StatusCode, innerException: exception);
        }
    }

    private HttpClient CreateClient()
    {
        var settings = Settings;
        if (!settings.IsConfigured)
        {
            throw new UpstreamException(Upstream, UpstreamFailure.NotConfigured,
                $"Lingarr is not configured, set {SubtitleOptions.SectionName}:Lingarr:Url.");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.BaseAddress = new Uri(settings.Url!.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
        }
        return httpClient;
    }

    // Lingarr's translation services speak two letter codes
    private static string LingarrLanguage(string language) => Languages.ToTwoLetter(language) ?? language;
}
