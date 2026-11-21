using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Radarr;

namespace Akira.Infrastructure.Interfaces.Integrations;

public interface IRadarrClient
{
    /// <summary>
    /// Retrieves every movie in Radarr, including its current file.
    /// </summary>
    Task<IReadOnlyList<RadarrMovie>> GetMoviesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves one movie with its current file. A movie Radarr does not have is an UpstreamException with NotFound.
    /// </summary>
    Task<RadarrMovie> GetMovieAsync(int movieId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a movie file from disk through Radarr.
    /// </summary>
    Task DeleteMovieFileAsync(int movieFileId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets movies monitored or unmonitored.
    /// </summary>
    Task SetMonitoredAsync(IReadOnlyList<int> movieIds, bool monitored, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a search for new copies of movies.
    /// </summary>
    Task SearchAsync(IReadOnlyList<int> movieIds, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves an image from Radarr's artwork cache for a movie.
    /// </summary>
    Task<ImageData> GetMediaCoverAsync(int movieId, string fileName, CancellationToken cancellationToken);
}
