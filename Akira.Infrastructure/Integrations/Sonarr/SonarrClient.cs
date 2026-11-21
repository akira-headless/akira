using System.Text.Json.Nodes;
using Akira.Core.Library;
using Akira.Infrastructure.Interfaces.Integrations;

namespace Akira.Infrastructure.Integrations.Sonarr;

public class SonarrClient : ISonarrClient
{
    private readonly HttpClient _httpClient;
    private readonly string _upstream;

    public SonarrClient(
        HttpClient httpClient,
        string upstream)
    {
        _httpClient = httpClient;
        _upstream = upstream;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SonarrSeries>> GetSeriesAsync(CancellationToken cancellationToken) =>
        await UpstreamHttp.GetJsonAsync<List<SonarrSeries>>(_httpClient, _upstream, "api/v3/series", cancellationToken);

    /// <inheritdoc />
    public Task<SonarrSeries> GetSeriesAsync(int seriesId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seriesId);
        return UpstreamHttp.GetJsonAsync<SonarrSeries>(_httpClient, _upstream, $"api/v3/series/{seriesId}", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SonarrEpisode>> GetEpisodesAsync(int seriesId, CancellationToken cancellationToken) =>
        await UpstreamHttp.GetJsonAsync<List<SonarrEpisode>>(_httpClient, _upstream,
            $"api/v3/episode?seriesId={seriesId}&includeImages=true", cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SonarrEpisodeFile>> GetEpisodeFilesAsync(int seriesId, CancellationToken cancellationToken) =>
        await UpstreamHttp.GetJsonAsync<List<SonarrEpisodeFile>>(_httpClient, _upstream,
            $"api/v3/episodefile?seriesId={seriesId}", cancellationToken);

    /// <inheritdoc />
    public Task DeleteEpisodeFileAsync(int episodeFileId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(episodeFileId);
        return UpstreamHttp.SendJsonAsync(_httpClient, _upstream, HttpMethod.Delete, $"api/v3/episodefile/{episodeFileId}", null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetEpisodesMonitoredAsync(IReadOnlyList<int> episodeIds, bool monitored, CancellationToken cancellationToken) =>
        UpstreamHttp.SendJsonAsync(_httpClient, _upstream, HttpMethod.Put, "api/v3/episode/monitor", new { episodeIds, monitored },
            cancellationToken);

    /// <inheritdoc />
    public async Task SetSeriesMonitoredAsync(int seriesId, int? seasonNumber, bool monitored, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seriesId);

        // Sonarr only takes the whole series resource back. Edited as JSON, so every field this
        // server does not model goes back exactly as Sonarr sent it.
        var series = await UpstreamHttp.GetJsonAsync<JsonObject>(_httpClient, _upstream, $"api/v3/series/{seriesId}", cancellationToken);
        if (seasonNumber is null)
        {
            series["monitored"] = monitored;
        }
        else
        {
            var seasons = series["seasons"] as JsonArray;
            var season = seasons?.OfType<JsonObject>().FirstOrDefault(candidate => candidate["seasonNumber"]?.GetValue<int>() == seasonNumber);
            if (season is null)
            {
                throw new UpstreamException(_upstream, UpstreamFailure.NotFound, $"{_upstream} has no season {seasonNumber} of series {seriesId}.");
            }

            season["monitored"] = monitored;
        }

        await UpstreamHttp.SendJsonAsync(_httpClient, _upstream, HttpMethod.Put, $"api/v3/series/{seriesId}", series, cancellationToken);
    }

    /// <inheritdoc />
    public Task SearchEpisodesAsync(IReadOnlyList<int> episodeIds, CancellationToken cancellationToken) =>
        UpstreamHttp.SendJsonAsync(_httpClient, _upstream, HttpMethod.Post, "api/v3/command", new { name = "EpisodeSearch", episodeIds },
            cancellationToken);

    /// <inheritdoc />
    public Task<ImageData> GetMediaCoverAsync(int seriesId, string fileName, CancellationToken cancellationToken)
    {
        // /MediaCover/... needs a browser session, the API route accepts the key
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seriesId);
        if (!Artwork.IsArrFileName(fileName))
        {
            throw new ArgumentException($"'{fileName}' is not a MediaCover file name.", nameof(fileName));
        }

        return UpstreamHttp.GetImageAsync(_httpClient, _upstream, $"api/v3/mediacover/{seriesId}/{fileName}", cancellationToken);
    }
}
