using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Sync;

namespace Akira.Infrastructure.Interfaces.Sync;

public interface ISeriesSync
{
    /// <summary>
    /// Synchronizes all series, seasons, episodes and their files from one Sonarr instance into the library.
    /// </summary>
    Task<SyncResult> SyncAsync(ArrInstanceOptions instance, CancellationToken cancellationToken);

    /// <summary>
    /// Synchronizes one series with its seasons and episodes from one Sonarr instance, and unlinks it when Sonarr no longer has it.
    /// </summary>
    Task<SyncResult> SyncSeriesAsync(ArrInstanceOptions instance, int seriesId, CancellationToken cancellationToken);
}
