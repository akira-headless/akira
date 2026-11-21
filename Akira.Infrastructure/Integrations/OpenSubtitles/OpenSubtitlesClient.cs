using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Akira.Contracts.Subtitles;
using Microsoft.Extensions.Options;
using Akira.Core;
using Akira.Core.Media;
using Akira.Infrastructure.Subtitles;

namespace Akira.Infrastructure.Integrations.OpenSubtitles;

/// <summary>
/// Searches and downloads subtitles from OpenSubtitles.com, with the household's own API key and account.
/// </summary>
public sealed class OpenSubtitlesClient : ISubtitleSource
{
    public const string HttpClientName = "opensubtitles";
    public const long MaxSubtitleBytes = 10 * 1024 * 1024;
    private const string Upstream = "OpenSubtitles";

    // Their two letter codes, except where they tell variants apart
    private static readonly Dictionary<string, string> Variants = new() { ["por"] = "pt-pt,pt-br", ["chi"] = "zh-cn,zh-tw" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<SubtitleOptions> _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _login = new(1, 1);
    private string? _token;
    private string? _tokenAccount;
    private DateTimeOffset _tokenExpires;

    public OpenSubtitlesClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<SubtitleOptions> options,
        TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _time = time;
    }

    public string Key => "opensubtitles";
    public string Name => "OpenSubtitles.com";
    public bool IsConfigured => Settings.IsConfigured;

    private OpenSubtitlesOptions Settings => _options.CurrentValue.OpenSubtitles;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleSearch search, CancellationToken cancellationToken)
    {
        var language = Variants.GetValueOrDefault(search.Language) ?? Languages.ToTwoLetter(search.Language);
        if (language is null)
        {
            return [];
        }

        var query = new List<string> { "languages=" + language };
        if (search.Type == MediaKind.Episode)
        {
            Add(query, "parent_tmdb_id", search.SeriesTmdbId);
            Add(query, "parent_imdb_id", ImdbNumber(search.SeriesImdbId));
            Add(query, "season_number", search.SeasonNumber);
            Add(query, "episode_number", search.EpisodeNumber);
        }
        else
        {
            Add(query, "tmdb_id", search.TmdbId);
            Add(query, "imdb_id", ImdbNumber(search.ImdbId));
        }
        Add(query, "moviehash", search.FileHash);

        using var httpClient = CreateClient();
        // OpenSubtitles redirects a query whose parameters are not in alphabetical order, and redirects are not followed
        query.Sort(StringComparer.Ordinal);
        var answer = await UpstreamHttp.GetJsonAsync<OpenSubtitlesSearch>(httpClient, Upstream, "subtitles?" + string.Join('&', query),
            cancellationToken);

        return (answer.Data ?? [])
            .Where(result => result.Attributes?.Files is { Count: > 0 })
            .Select(result => result.Attributes!)
            .Select(attributes => new SubtitleCandidate(
                attributes.Files![0].FileId.ToString(CultureInfo.InvariantCulture),
                Languages.Normalize(attributes.Language) ?? search.Language,
                attributes.Release,
                attributes.DownloadCount,
                attributes.HearingImpaired,
                attributes.ForeignPartsOnly,
                attributes.MoviehashMatch,
                FormatOf(attributes.Files[0].FileName)))
            // A file hash match is timed for exactly this file; after that the most downloaded is the safest bet
            .OrderByDescending(candidate => candidate.HashMatch)
            .ThenByDescending(candidate => candidate.Downloads)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SubtitleDownload> DownloadAsync(string reference, CancellationToken cancellationToken)
    {
        using var httpClient = CreateClient();
        OpenSubtitlesDownload link;
        string? token = null;
        try
        {
            try
            {
                token = await AuthorizeAsync(httpClient, renew: false, cancellationToken);
                link = await AskLinkAsync(httpClient, reference, cancellationToken);
            }
            catch (UpstreamException exception) when (exception.Failure == UpstreamFailure.Unauthorized && token is not null)
            {
                // The token was ended early (a password change, a logout elsewhere): once more with a new one
                token = await AuthorizeAsync(httpClient, renew: true, cancellationToken);
                link = await AskLinkAsync(httpClient, reference, cancellationToken);
            }
        }
        catch (UpstreamException exception) when (exception.StatusCode == HttpStatusCode.NotAcceptable)
        {
            // Their answer when the day's downloads are used up
            var advice = token is null
                ? $"Set {SubtitleOptions.SectionName}:OpenSubtitles:Username and Password for an account's quota."
                : "Try again tomorrow.";
            throw new UpstreamException(Upstream, UpstreamFailure.RateLimited,
                "OpenSubtitles allows no more downloads today for this account. " + advice, exception.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(link.Link))
        {
            throw new UpstreamException(Upstream, UpstreamFailure.Malformed, "OpenSubtitles answered a download without a link.");
        }

        // The link comes from their answer: it has to be theirs and over https, and it is fetched without the API key or token
        if (!Uri.TryCreate(link.Link, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || !IsTheirs(url.Host))
        {
            throw new UpstreamException(Upstream, UpstreamFailure.Malformed, "OpenSubtitles answered a download link that is not theirs.");
        }

        using var plain = _httpClientFactory.CreateClient(HttpClientName);
        plain.DefaultRequestHeaders.UserAgent.ParseAdd($"Akira v{ServerVersion.Number}");
        var content = await UpstreamHttp.GetBytesAsync(plain, Upstream, url.AbsoluteUri, MaxSubtitleBytes, cancellationToken);
        return new SubtitleDownload(content, "srt");
    }

    private static string FormatOf(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? "").TrimStart('.');
        return extension.Length > 0 ? extension : "srt";
    }

    private static bool IsTheirs(string host) =>
        host is "opensubtitles.com" or "opensubtitles.org"
        || host.EndsWith(".opensubtitles.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".opensubtitles.org", StringComparison.OrdinalIgnoreCase);

    private static Task<OpenSubtitlesDownload> AskLinkAsync(HttpClient httpClient, string reference, CancellationToken cancellationToken)
    {
        // The reference comes back from a client, which may send anything
        if (!long.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var fileId))
        {
            throw new UpstreamException(Upstream, UpstreamFailure.Malformed, $"{reference} is not a subtitle {Upstream} listed.");
        }

        return UpstreamHttp.PostJsonAsync<OpenSubtitlesDownload>(httpClient, Upstream, "download",
            new { file_id = fileId, sub_format = "srt" }, cancellationToken);
    }

    // The account's token on the client, when an account is set; renew drops the one there is
    private async Task<string?> AuthorizeAsync(HttpClient httpClient, bool renew, CancellationToken cancellationToken)
    {
        var token = await LoginAsync(httpClient, renew, cancellationToken);
        httpClient.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return token;
    }

    private async Task<string?> LoginAsync(HttpClient httpClient, bool renew, CancellationToken cancellationToken)
    {
        var settings = Settings;
        if (string.IsNullOrWhiteSpace(settings.Username))
        {
            return null;
        }

        await _login.WaitAsync(cancellationToken);
        try
        {
            // A token is for the account it was made with, which can change while the server runs
            var account = settings.Username + "\n" + settings.Password;
            if (!renew && _token is not null && _tokenAccount == account && _tokenExpires > _time.GetUtcNow())
            {
                return _token;
            }

            _token = null;
            httpClient.DefaultRequestHeaders.Authorization = null;

            var login = await UpstreamHttp.PostJsonAsync<OpenSubtitlesLogin>(httpClient, Upstream, "login",
                new { username = settings.Username, password = settings.Password }, cancellationToken);
            if (string.IsNullOrWhiteSpace(login.Token))
            {
                throw new UpstreamException(Upstream, UpstreamFailure.Malformed, "OpenSubtitles answered the login without a token.");
            }

            // Tokens last 24 hours; renewed well before
            _token = login.Token;
            _tokenAccount = account;
            _tokenExpires = _time.GetUtcNow().AddHours(12);
            return _token;
        }
        finally
        {
            _login.Release();
        }
    }

    private HttpClient CreateClient()
    {
        var settings = Settings;
        if (!settings.IsConfigured)
        {
            throw new UpstreamException(Upstream, UpstreamFailure.NotConfigured,
                $"OpenSubtitles is not configured, set {SubtitleOptions.SectionName}:OpenSubtitles:ApiKey.");
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.BaseAddress = new Uri(settings.Url.TrimEnd('/') + "/");
        httpClient.DefaultRequestHeaders.Add("Api-Key", settings.ApiKey);
        // OpenSubtitles refuses requests without an application name and version
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Akira v{ServerVersion.Number}");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return httpClient;
    }

    private static void Add(List<string> query, string name, object? value)
    {
        if (value is not null && value.ToString() is { Length: > 0 } text)
        {
            query.Add(name + "=" + Uri.EscapeDataString(text));
        }
    }

    private static string? ImdbNumber(string? imdbId)
    {
        if (imdbId is not { Length: > 2 } || !imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return imdbId[2..].TrimStart('0');
    }
}
