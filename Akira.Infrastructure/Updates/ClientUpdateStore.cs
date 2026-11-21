using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Integrations;

namespace Akira.Infrastructure.Updates;

/// <summary>
/// Downloads the newest build of a client app from its source when an app asks for a check, and keeps one per platform on disk.
/// </summary>
public sealed partial class ClientUpdateStore
{
    public const string HttpClientName = "client-updates";

    // A Flatpak with Java and mpv inside is a few hundred MB
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxManifestBytes = 64 * 1024;
    private const int MaxRedirects = 5;
    private const string CurrentFile = "current.json";
    private const string PartialFile = ".partial";
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClients;
    private readonly IOptionsMonitor<ClientUpdateOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ClientUpdateStore> _logger;
    private readonly SemaphoreSlim _checking = new(1, 1);

    public ClientUpdateStore(
        IHttpClientFactory httpClients,
        IOptionsMonitor<ClientUpdateOptions> options,
        TimeProvider time,
        ILogger<ClientUpdateStore> logger)
    {
        _httpClients = httpClients;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the build the server holds for a platform, or null when that platform has no source or nothing came yet.
    /// </summary>
    public StoredClientUpdate? Find(string platform)
    {
        if (!_options.CurrentValue.Sources.ContainsKey(platform) || !ClientUpdateOptionsValidator.PlatformPattern().IsMatch(platform))
        {
            return null;
        }

        var current = Path.Combine(Folder(platform), CurrentFile);
        try
        {
            var stored = JsonSerializer.Deserialize<StoredClientUpdate>(File.ReadAllText(current), SerializerOptions);
            if (stored is null)
            {
                return null;
            }

            var path = Path.Combine(Folder(platform), stored.Build.ToString(), stored.FileName);
            return File.Exists(path) ? stored with { Platform = platform, Path = path } : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks a platform's source for a newer build and downloads it, then returns the build the server holds. Null when
    /// the platform has no source; a source that fails throws and keeps the build there was.
    /// </summary>
    public async Task<StoredClientUpdate?> CheckAsync(string platform, CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.Sources.TryGetValue(platform, out var source))
        {
            return null;
        }

        // The download goes on when the app stops waiting for it (it has its own time limit), so a large build from a slow
        // source still arrives and the next press finds it
        await CheckSourceAsync(platform, source).WaitAsync(cancellationToken);
        return Find(platform);
    }

    private async Task CheckSourceAsync(string platform, ClientUpdateSource source)
    {
        // One check at a time: two devices pressing at once would download the same build twice into one file
        await _checking.WaitAsync();
        try
        {
            await DownloadNewerAsync(platform, source, CancellationToken.None);
        }
        catch (Exception exception) when (exception is UpstreamException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Checking the {Platform} app for an update failed: {Reason}", platform, exception.Message);
            throw;
        }
        finally
        {
            _checking.Release();
        }
    }

    private async Task DownloadNewerAsync(string platform, ClientUpdateSource source, CancellationToken cancellationToken)
    {
        var upstream = $"The {platform} update source";
        var manifestUrl = new Uri(source.ManifestUrl);
        using var httpClient = _httpClients.CreateClient(HttpClientName);

        ClientUpdateManifest manifest;
        using (var response = await SendAsync(httpClient, upstream, manifestUrl, source.Authorization, cancellationToken))
        {
            manifest = Validate(upstream, await ReadManifestAsync(response, upstream, cancellationToken));
        }

        if (Find(platform)?.Build == manifest.Build)
        {
            return;
        }

        // Relative to the configured url and not to where a redirect led, which for a GitHub release is its storage
        var fileUrl = new Uri(manifestUrl, manifest.File);
        var fileName = FileName(fileUrl);
        var folder = Folder(platform);
        Directory.CreateDirectory(folder);
        var partial = Path.Combine(folder, PartialFile);
        try
        {
            long size;
            string sha256;
            using (var response = await SendAsync(httpClient, upstream, fileUrl, source.Authorization, cancellationToken))
            {
                (size, sha256) = await DownloadAsync(response, partial, upstream, fileUrl, cancellationToken);
            }

            if (!string.Equals(sha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpstreamException(upstream, UpstreamFailure.Malformed,
                    $"{upstream} sent build {manifest.Build} with SHA-256 {sha256}, the manifest says {manifest.Sha256}.");
            }

            var buildFolder = Path.Combine(folder, manifest.Build.ToString());
            Directory.CreateDirectory(buildFolder);
            File.Move(partial, Path.Combine(buildFolder, fileName), overwrite: true);

            var stored = new StoredClientUpdate(platform, manifest.Build, manifest.Version ?? manifest.Build.ToString(), fileName, "", size,
                sha256, _time.GetUtcNow());
            var next = Path.Combine(folder, CurrentFile + ".next");
            await File.WriteAllTextAsync(next, JsonSerializer.Serialize(stored, SerializerOptions), cancellationToken);
            File.Move(next, Path.Combine(folder, CurrentFile), overwrite: true);
            _logger.LogInformation("Holding the {Platform} app build {Build} ({Version})", platform, stored.Build, stored.Version);

            RemoveOlderBuilds(folder, buildFolder);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    private void RemoveOlderBuilds(string folder, string keep)
    {
        foreach (var old in Directory.GetDirectories(folder).Where(path => path != keep))
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (IOException exception)
            {
                // On Windows a file being downloaded cannot be removed; the next new build removes it
                _logger.LogDebug("Could not remove the old build {Folder} yet: {Reason}", old, exception.Message);
            }
        }
    }

    private string Folder(string platform) => Path.Combine(_options.CurrentValue.ResolvePath(), platform);

    private static ClientUpdateManifest Validate(string upstream, ClientUpdateManifest manifest)
    {
        if (manifest.Build <= 0 || string.IsNullOrWhiteSpace(manifest.File) || manifest.Sha256 is null || !Sha256Pattern().IsMatch(manifest.Sha256))
        {
            throw new UpstreamException(upstream, UpstreamFailure.Malformed,
                $"{upstream} sent a manifest without a positive build, a file or a SHA-256 of 64 hex digits.");
        }

        return manifest;
    }

    // Only the last segment of the url, and only characters that are safe in a file name everywhere
    private static string FileName(Uri url)
    {
        var name = UnsafeFileCharacters().Replace(Uri.UnescapeDataString(url.Segments.LastOrDefault() ?? ""), "-").Trim('.', '-');
        return name.Length == 0 || name == CurrentFile ? "client" : name;
    }

    private static async Task<ClientUpdateManifest> ReadManifestAsync(HttpResponseMessage response, string upstream, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxManifestBytes)
        {
            throw new UpstreamException(upstream, UpstreamFailure.Malformed, $"{upstream} sent a manifest larger than 64 KB.");
        }

        try
        {
            await using var stream = new BoundedReadStream(await response.Content.ReadAsStreamAsync(cancellationToken), MaxManifestBytes);
            return await JsonSerializer.DeserializeAsync<ClientUpdateManifest>(stream, SerializerOptions, cancellationToken)
                   ?? throw new JsonException("The manifest was null.");
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // A Gitea or proxy login page arrives as a 200 with HTML
            throw new UpstreamException(upstream, UpstreamFailure.Malformed,
                $"{upstream} did not send a manifest that could be read. Check that the url points at update.json.", innerException: exception);
        }
    }

    private static async Task<(long Size, string Sha256)> DownloadAsync(HttpResponseMessage response, string path, string upstream, Uri url,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxFileBytes)
        {
            throw TooLarge(upstream);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var output = File.Create(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long size = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, timeout.Token)) > 0)
            {
                size += read;
                if (size > MaxFileBytes)
                {
                    throw TooLarge(upstream);
                }

                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            return (size, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamException(upstream, UpstreamFailure.Timeout,
                $"{upstream} did not finish sending {url} within {DownloadTimeout.TotalMinutes:0} minutes.", innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new UpstreamException(upstream, UpstreamFailure.Unreachable, $"{upstream} broke off sending {url}: {exception.Message}",
                innerException: exception);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient httpClient, string upstream, Uri url, string? authorization,
        CancellationToken cancellationToken)
    {
        // Redirects are followed here and not by the handler: GitHub sends release files on to its storage, and that
        // host must not get the credential meant for the source
        var target = url;
        for (var hop = 0;; hop++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target);
                if (!string.IsNullOrEmpty(authorization) && target.Scheme == url.Scheme && target.Authority == url.Authority)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", authorization);
                }

                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpstreamException(upstream, UpstreamFailure.Timeout,
                    $"{upstream} did not answer {target} within {httpClient.Timeout.TotalSeconds:0} seconds.", innerException: exception);
            }
            catch (HttpRequestException exception)
            {
                throw new UpstreamException(upstream, UpstreamFailure.Unreachable, $"{upstream} could not be reached: {exception.Message}",
                    innerException: exception);
            }

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                var next = new Uri(target, location);
                if (hop >= MaxRedirects || (next.Scheme != Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttp)
                                         || (target.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp))
                {
                    throw new UpstreamException(upstream, UpstreamFailure.Malformed,
                        $"{upstream} redirected {url} too often, or from https to http.", response.StatusCode);
                }

                target = next;
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var statusCode = response.StatusCode;
            response.Dispose();
            var (failure, message) = statusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => (UpstreamFailure.Unauthorized,
                    $"{upstream} refused {target}. Check its Authorization setting."),
                HttpStatusCode.NotFound => (UpstreamFailure.NotFound, $"{upstream} has nothing at {target}."),
                _ => (UpstreamFailure.UnexpectedStatus, $"{upstream} answered {target} with {(int)statusCode} {statusCode}.")
            };
            throw new UpstreamException(upstream, failure, message, statusCode);
        }
    }

    private static UpstreamException TooLarge(string upstream) =>
        new(upstream, UpstreamFailure.Malformed, $"{upstream} sent a file larger than {MaxFileBytes / (1024 * 1024 * 1024)} GB.");

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex UnsafeFileCharacters();
}
