using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Sonarr;

namespace Akira.Infrastructure.Interfaces.Integrations;

public interface ISonarrClient
{
    /// <summary>
    /// Retrieves every series in Sonarr, including its seasons.
    /// </summary>
    Task<IReadOnlyList<SonarrSeries>> GetSeriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves one series with its seasons. A series Sonarr does not have is an UpstreamException with NotFound.
    /// </summary>
    Task<SonarrSeries> GetSeriesAsync(int seriesId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all episodes of a series, with their images.
    /// </summary>
    Task<IReadOnlyList<SonarrEpisode>> GetEpisodesAsync(int seriesId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all episode files of a series.
    /// </summary>
    Task<IReadOnlyList<SonarrEpisodeFile>> GetEpisodeFilesAsync(int seriesId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an episode file from disk through Sonarr.
    /// </summary>
    Task DeleteEpisodeFileAsync(int episodeFileId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets episodes monitored or unmonitored.
    /// </summary>
    Task SetEpisodesMonitoredAsync(IReadOnlyList<int> episodeIds, bool monitored, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a whole series, or one of its seasons, monitored or unmonitored.
    /// </summary>
    Task SetSeriesMonitoredAsync(int seriesId, int? seasonNumber, bool monitored, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a search for new copies of episodes.
    /// </summary>
    Task SearchEpisodesAsync(IReadOnlyList<int> episodeIds, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves an image from Sonarr's artwork cache for a series.
    /// </summary>
    Task<ImageData> GetMediaCoverAsync(int seriesId, string fileName, CancellationToken cancellationToken);
}
