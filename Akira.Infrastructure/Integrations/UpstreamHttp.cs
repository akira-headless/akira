using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Akira.Infrastructure.Integrations;

/// <summary>
/// Sends requests to an upstream and turns every way they can fail into an UpstreamException.
/// </summary>
public static class UpstreamHttp
{
    public const long MaxImageBytes = 20 * 1024 * 1024;

    // Many times the movie list of a large library, and still less than the memory an endless answer would take
    public const long MaxJsonBytes = 256 * 1024 * 1024;

    public static readonly IReadOnlySet<string> ImageContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // Redirects are never followed. From Radarr or Sonarr a redirect is a login page, which would
    // otherwise arrive as a 200 with HTML, and from an artwork host it could lead off the allow list.
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler { AllowAutoRedirect = false };

    public static async Task<T> GetJsonAsync<T>(
        HttpClient httpClient,
        string upstream,
        string relativeUrl,
        CancellationToken cancellationToken) where T : class
    {
        using var response = await SendAsync(httpClient, upstream, HttpMethod.Get, relativeUrl, null, cancellationToken);
        return await ReadJsonAsync<T>(httpClient, response, upstream, relativeUrl, cancellationToken);
    }

    /// <summary>
    /// Posts a JSON body and reads the JSON answer.
    /// </summary>
    public static async Task<T> PostJsonAsync<T>(
        HttpClient httpClient,
        string upstream,
        string relativeUrl,
        object body,
        CancellationToken cancellationToken) where T : class
    {
        using var content = JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        using var response = await SendAsync(httpClient, upstream, HttpMethod.Post, relativeUrl, content, cancellationToken);
        return await ReadJsonAsync<T>(httpClient, response, upstream, relativeUrl, cancellationToken);
    }

    /// <summary>
    /// Downloads a file of at most maxBytes, such as a subtitle.
    /// </summary>
    public static async Task<byte[]> GetBytesAsync(
        HttpClient httpClient,
        string upstream,
        string url,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(httpClient, upstream, HttpMethod.Get, url, null, cancellationToken);
        return await ReadBoundedAsync(httpClient, response, upstream, url, maxBytes, () => TooLargeFile(upstream, maxBytes, response.StatusCode),
            cancellationToken);
    }

    // At most maxBytes of a body, read under the client's timeout like the request before it
    private static Task<byte[]> ReadBoundedAsync(HttpClient httpClient, HttpResponseMessage response, string upstream, string url, long maxBytes,
        Func<UpstreamException> tooLarge, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw tooLarge();
        }

        return BodyAsync(httpClient, upstream, url, token => ReadAtMostAsync(response, maxBytes, tooLarge, token), cancellationToken);
    }

    private static async Task<byte[]> ReadAtMostAsync(HttpResponseMessage response, long maxBytes, Func<UpstreamException> tooLarge,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw tooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Reads a body with the client's timeout still running: the request's own only covers the headers, after which an
    /// upstream that stalls or breaks off halfway would otherwise hang or throw something other than an UpstreamException.
    /// </summary>
    private static async Task<T> BodyAsync<T>(HttpClient httpClient, string upstream, string url, Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (httpClient.Timeout != Timeout.InfiniteTimeSpan)
        {
            timeout.CancelAfter(httpClient.Timeout);
        }

        try
        {
            return await read(timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamException(upstream, UpstreamFailure.Timeout,
                $"{upstream} did not finish sending {url} within {httpClient.Timeout.TotalSeconds:0} seconds.", innerException: exception);
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new UpstreamException(upstream, UpstreamFailure.Unreachable, $"{upstream} broke off sending {url}: {exception.Message}",
                innerException: exception);
        }
    }

    private static UpstreamException TooLargeFile(string upstream, long maxBytes, HttpStatusCode statusCode) =>
        new(upstream, UpstreamFailure.Malformed, $"{upstream} sent a file larger than {maxBytes / (1024 * 1024)} MB.", statusCode);

    private static async Task<T> ReadJsonAsync<T>(
        HttpClient httpClient,
        HttpResponseMessage response,
        string upstream,
        string relativeUrl,
        CancellationToken cancellationToken) where T : class
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            throw new UpstreamException(upstream, UpstreamFailure.Malformed,
                $"{upstream} answered {relativeUrl} with '{mediaType ?? "no content type"}' instead of JSON. " +
                "Check that the url points at the application and not at a login page or proxy.",
                response.StatusCode);
        }

        if (response.Content.Headers.ContentLength > MaxJsonBytes)
        {
            throw TooLargeJson(upstream, relativeUrl, response.StatusCode);
        }

        try
        {
            return await BodyAsync(httpClient, upstream, relativeUrl,
                token => ReadJsonBodyAsync<T>(response, () => TooLargeJson(upstream, relativeUrl, response.StatusCode), token), cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new UpstreamException(upstream, UpstreamFailure.Malformed,
                $"{upstream} answered {relativeUrl} with JSON that could not be read.",
                response.StatusCode, innerException: exception);
        }
    }

    private static async Task<T> ReadJsonBodyAsync<T>(HttpResponseMessage response, Func<UpstreamException> tooLarge,
        CancellationToken cancellationToken) where T : class
    {
        // Read as it arrives and counted on the way, as a list without a length is only too long once it is
        await using var stream = new BoundedReadStream(await response.Content.ReadAsStreamAsync(cancellationToken), MaxJsonBytes);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = response.Content.Headers.ContentType;
        try
        {
            return await content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken) ?? throw new JsonException("The response body was null.");
        }
        catch (IOException) when (stream.Exceeded)
        {
            throw tooLarge();
        }
    }

    private static UpstreamException TooLargeJson(string upstream, string url, HttpStatusCode statusCode) =>
        new(upstream, UpstreamFailure.Malformed, $"{upstream} answered {url} with more than {MaxJsonBytes / (1024 * 1024)} MB of JSON.", statusCode);

    public static async Task<ImageData> GetImageAsync(
        HttpClient httpClient,
        string upstream,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(httpClient, upstream, HttpMethod.Get, relativeUrl, null, cancellationToken);

        // The content type is decided here, never passed through from the upstream
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is null || !ImageContentTypes.Contains(contentType))
        {
            throw new UpstreamException(upstream, UpstreamFailure.Malformed,
                $"{upstream} answered an image request with '{contentType ?? "no content type"}'.", response.StatusCode);
        }

        var bytes = await ReadBoundedAsync(httpClient, response, upstream, relativeUrl, MaxImageBytes, () => TooLarge(upstream, response.StatusCode),
            cancellationToken);
        return new ImageData(bytes, contentType.ToLowerInvariant());
    }

    /// <summary>
    /// Sends a request that changes something upstream, with an optional JSON body, and ignores what it answers.
    /// </summary>
    public static async Task SendJsonAsync(
        HttpClient httpClient,
        string upstream,
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        using var content = body is null ? null : JsonContent.Create(body, body.GetType(), options: SerializerOptions);
        using var response = await SendAsync(httpClient, upstream, method, relativeUrl, content, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        string upstream,
        HttpMethod method,
        string relativeUrl,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (httpClient.BaseAddress is null && !Uri.IsWellFormedUriString(relativeUrl, UriKind.Absolute))
        {
            throw new UpstreamException(upstream, UpstreamFailure.NotConfigured, $"{upstream} is not configured.");
        }

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(method, relativeUrl) { Content = content };
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpstreamException(upstream, UpstreamFailure.Timeout,
                $"{upstream} did not answer {relativeUrl} within {httpClient.Timeout.TotalSeconds:0} seconds.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new UpstreamException(upstream, UpstreamFailure.Unreachable,
                $"{upstream} could not be reached: {exception.Message}", innerException: exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var statusCode = response.StatusCode;
        var retryAfter = response.Headers.RetryAfter?.Delta;
        response.Dispose();

        var failure = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => UpstreamFailure.Unauthorized,
            HttpStatusCode.NotFound => UpstreamFailure.NotFound,
            HttpStatusCode.TooManyRequests => UpstreamFailure.RateLimited,
            _ => UpstreamFailure.UnexpectedStatus
        };

        var message = failure switch
        {
            UpstreamFailure.Unauthorized => $"{upstream} rejected the API key.",
            UpstreamFailure.NotFound => $"{upstream} has nothing at {relativeUrl}.",
            UpstreamFailure.RateLimited => $"{upstream} is rate limiting requests.",
            _ => $"{upstream} answered {relativeUrl} with {(int)statusCode} {statusCode}."
        };

        throw new UpstreamException(upstream, failure, message, statusCode, retryAfter);
    }

    private static UpstreamException TooLarge(string upstream, HttpStatusCode statusCode) =>
        new(upstream, UpstreamFailure.Malformed,
            $"{upstream} sent an image larger than {MaxImageBytes / (1024 * 1024)} MB.", statusCode);
}
