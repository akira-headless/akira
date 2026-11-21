using Akira.Infrastructure.Sync;

namespace Akira.Infrastructure.Interfaces.Sync;

public interface ILibrarySyncService
{
    /// <summary>
    /// Runs a full sync of every configured instance, then removes the items no instance holds any more.
    /// </summary>
    Task<LibrarySyncReport> SyncAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Synchronizes one movie or series of one instance, as a webhook asks for, then removes what no instance holds any more.
    /// </summary>
    Task<LibrarySyncReport> RefreshAsync(string instanceKey, int arrId, CancellationToken cancellationToken);
}
