using Akira.Infrastructure.Interfaces.Sync;
using Akira.Infrastructure.Sync;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using Akira.Server.Webhooks;

namespace Akira.Server.Services.Events;

public class LibraryRefreshService : ILibraryRefreshService
{
    private readonly ILibrarySyncService _sync;
    private readonly ILibraryEventService _events;
    private readonly ProbeTrigger _probeTrigger;

    public LibraryRefreshService(
        ILibrarySyncService sync,
        ILibraryEventService events,
        ProbeTrigger probeTrigger)
    {
        _sync = sync;
        _events = events;
        _probeTrigger = probeTrigger;
    }

    /// <inheritdoc />
    public async Task RefreshAsync(IReadOnlyCollection<LibraryRefresh> refreshes, CancellationToken cancellationToken)
    {
        var results = new List<SyncResult>();
        var removed = new List<ItemChange>();
        foreach (var refresh in refreshes.Distinct())
        {
            var report = await _sync.RefreshAsync(refresh.InstanceKey, refresh.ArrId, cancellationToken);
            results.AddRange(report.Instances);
            removed.AddRange(report.Removed);
        }

        await _events.PublishSyncAsync(new LibrarySyncReport(results, removed), ScanKind.Refresh, cancellationToken);
        if (results.Any(result => result.Added + result.Updated > 0))
        {
            _probeTrigger.Request();
        }
    }
}
