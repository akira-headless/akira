using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Sync;

namespace Akira.Infrastructure.Interfaces.Sync;

public interface IMovieSync
{
    /// <summary>
    /// Synchronizes all movies and their files from one Radarr instance into the library.
    /// </summary>
    Task<SyncResult> SyncAsync(ArrInstanceOptions instance, CancellationToken cancellationToken);

    /// <summary>
    /// Synchronizes one movie from one Radarr instance, and unlinks it when Radarr no longer has it.
    /// </summary>
    Task<SyncResult> SyncMovieAsync(ArrInstanceOptions instance, int movieId, CancellationToken cancellationToken);
}
