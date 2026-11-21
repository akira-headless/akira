using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Talks to the Trakt API as the app in TraktOptions, on behalf of a user's access token.
/// </summary>
public sealed class TraktClient : ITraktClient
{
    public const string HttpClientName = "trakt";
    private const string Upstream = "Trakt";

    // Trakt's out of band redirect, which a device flow token refresh has to name
    private const string RedirectUri = "urn:ietf:wg:oauth:2.0:oob";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TraktOptions> _options;

    public TraktClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<TraktOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public bool IsConfigured => _options.CurrentValue.IsConfigured;

    /// <inheritdoc />
    public async Task<TraktDeviceCode> StartDeviceAsync(CancellationToken cancellationToken)
    {
        using var httpClient = CreateClient(null);
        return await UpstreamHttp.PostJsonAsync<TraktDeviceCode>(httpClient, Upstream, "oauth/device/code",
            new { client_id = _options.CurrentValue.ClientId }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TraktPoll> PollDeviceAsync(string deviceCode, CancellationToken cancellationToken)
    {
        var settings = _options.CurrentValue;
        using var httpClient = CreateClient(null);
        try
        {
            var token = await UpstreamHttp.PostJsonAsync<TraktToken>(httpClient, Upstream, "oauth/device/token",
                new { code = deviceCode, client_id = settings.ClientId, client_secret = settings.ClientSecret }, cancellationToken);
            return new TraktPoll(TraktPollStatus.Approved, token);
        }
        catch (UpstreamException exception) when (DeviceFlowState(exception) is { } state)
        {
            return new TraktPoll(state);
        }
    }

    // The device flow answers with a status per state instead of a body. Any other failure is not caught, so it keeps
    // its stack trace
    private static TraktPollStatus? DeviceFlowState(UpstreamException exception)
    {
        if (exception.StatusCode is not { } status)
        {
            return null;
        }

        return (int)status switch
        {
            400 => TraktPollStatus.Pending,
            429 => TraktPollStatus.SlowDown,
            404 or 409 => TraktPollStatus.Invalid,
            410 => TraktPollStatus.Expired,
            418 => TraktPollStatus.Denied,
            _ => null
        };
    }

    /// <inheritdoc />
    public async Task<TraktToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var settings = _options.CurrentValue;
        using var httpClient = CreateClient(null);
        return await UpstreamHttp.PostJsonAsync<TraktToken>(httpClient, Upstream, "oauth/token", new
        {
            refresh_token = refreshToken,
            client_id = settings.ClientId,
            client_secret = settings.ClientSecret,
            redirect_uri = RedirectUri,
            grant_type = "refresh_token"
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string accessToken, CancellationToken cancellationToken)
    {
        var settings = _options.CurrentValue;
        using var httpClient = CreateClient(null);
        await UpstreamHttp.SendJsonAsync(httpClient, Upstream, HttpMethod.Post, "oauth/revoke",
            new { token = accessToken, client_id = settings.ClientId, client_secret = settings.ClientSecret }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> GetUsernameAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var httpClient = CreateClient(accessToken);
        var userSettings = await UpstreamHttp.GetJsonAsync<TraktUserSettings>(httpClient, Upstream, "users/settings", cancellationToken);
        return userSettings.User.Username;
    }

    /// <inheritdoc />
    public async Task ScrobbleAsync(string accessToken, string action, object body, CancellationToken cancellationToken)
    {
        using var httpClient = CreateClient(accessToken);
        try
        {
            await UpstreamHttp.SendJsonAsync(httpClient, Upstream, HttpMethod.Post, $"scrobble/{action}", body, cancellationToken);
        }
        catch (UpstreamException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // The same item was scrobbled a moment ago, Trakt already has it
        }
    }

    /// <inheritdoc />
    public async Task AddHistoryAsync(string accessToken, object body, CancellationToken cancellationToken)
    {
        using var httpClient = CreateClient(accessToken);
        await UpstreamHttp.SendJsonAsync(httpClient, Upstream, HttpMethod.Post, "sync/history", body, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveHistoryAsync(string accessToken, object body, CancellationToken cancellationToken)
    {
        using var httpClient = CreateClient(accessToken);
        await UpstreamHttp.SendJsonAsync(httpClient, Upstream, HttpMethod.Post, "sync/history/remove", body, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TraktWatchedMovie>> GetWatchedMoviesAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var httpClient = CreateClient(accessToken);
        return await UpstreamHttp.GetJsonAsync<List<TraktWatchedMovie>>(httpClient, Upstream, "sync/watched/movies", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TraktWatchedShow>> GetWatchedShowsAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var httpClient = CreateClient(accessToken);
        return await UpstreamHttp.GetJsonAsync<List<TraktWatchedShow>>(httpClient, Upstream, "sync/watched/shows", cancellationToken);
    }

    private HttpClient CreateClient(string? accessToken)
    {
        var settings = _options.CurrentValue;
        if (!settings.IsConfigured)
        {
            throw new UpstreamException(Upstream, UpstreamFailure.NotConfigured,
                $"Trakt is not configured, set {TraktOptions.SectionName}:{nameof(TraktOptions.ClientId)} and {nameof(TraktOptions.ClientSecret)}.");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.BaseAddress = new Uri(settings.Url.TrimEnd('/') + "/");
        httpClient.DefaultRequestHeaders.Add("trakt-api-version", "2");
        httpClient.DefaultRequestHeaders.Add("trakt-api-key", settings.ClientId);
        if (accessToken is not null)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return httpClient;
    }
}
