using Akira.Core.Library;
using Akira.Infrastructure.Interfaces.Integrations;

namespace Akira.Infrastructure.Integrations.Radarr;

public class RadarrClient : IRadarrClient
{
    private readonly HttpClient _httpClient;
    private readonly string _upstream;

    public RadarrClient(
        HttpClient httpClient,
        string upstream)
    {
        _httpClient = httpClient;
        _upstream = upstream;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RadarrMovie>> GetMoviesAsync(CancellationToken cancellationToken) =>
        await UpstreamHttp.GetJsonAsync<List<RadarrMovie>>(_httpClient, _upstream, "api/v3/movie", cancellationToken);

    /// <inheritdoc />
    public Task<RadarrMovie> GetMovieAsync(int movieId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(movieId);
        return UpstreamHttp.GetJsonAsync<RadarrMovie>(_httpClient, _upstream, $"api/v3/movie/{movieId}", cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteMovieFileAsync(int movieFileId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(movieFileId);
        return UpstreamHttp.SendJsonAsync(_httpClient, _upstream, HttpMethod.Delete, $"api/v3/moviefile/{movieFileId}", null, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetMonitoredAsync(IReadOnlyList<int> movieIds, bool monitored, CancellationToken cancellationToken) =>
        UpstreamHttp.SendJsonAsync(_httpClient, _upstream, HttpMethod.Put, "api/v3/movie/editor", new { movieIds, monitored }, cancellationToken);

    /// <inheritdoc />
    public Task SearchAsync(IReadOnlyList<int> movieIds, CancellationToken cancellationToken) =>
        UpstreamHttp.SendJsonAsync(_httpClient, _upstream, HttpMethod.Post, "api/v3/command", new { name = "MoviesSearch", movieIds },
            cancellationToken);

    /// <inheritdoc />
    public Task<ImageData> GetMediaCoverAsync(int movieId, string fileName, CancellationToken cancellationToken)
    {
        // /MediaCover/... needs a browser session, the API route accepts the key
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(movieId);
        if (!Artwork.IsArrFileName(fileName))
        {
            throw new ArgumentException($"'{fileName}' is not a MediaCover file name.", nameof(fileName));
        }

        return UpstreamHttp.GetImageAsync(_httpClient, _upstream, $"api/v3/mediacover/{movieId}/{fileName}", cancellationToken);
    }
}
