using Akira.Infrastructure.Interfaces.Probing;
using Akira.Infrastructure.Sync;
using Akira.Server.Models.Events;

namespace Akira.Server.Interfaces.Services;

public interface ILibraryEventService
{
    /// <summary>
    /// Publishes what a full sync or a refresh changed in the library.
    /// </summary>
    Task PublishSyncAsync(LibrarySyncReport report, ScanKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes the items whose tracks a probe pass or rescan changed.
    /// </summary>
    Task PublishProbeAsync(ProbeRunResult probe, RescanResult? rescan, CancellationToken cancellationToken);
}
