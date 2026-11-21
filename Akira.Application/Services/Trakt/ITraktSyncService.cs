namespace Akira.Server.Services.Trakt;

public interface ITraktSyncService
{
    /// <summary>
    /// Sends what happened since the last pass to the Trakt accounts of the users it happened to. True when there is more right away.
    /// </summary>
    Task<bool> SyncPendingAsync(CancellationToken cancellationToken);
}
