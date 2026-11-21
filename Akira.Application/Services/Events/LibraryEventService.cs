using Microsoft.EntityFrameworkCore;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Interfaces.Probing;
using Akira.Infrastructure.Sync;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;

namespace Akira.Server.Services.Events;

public class LibraryEventService : ILibraryEventService
{
    private readonly AkiraDbContext _dbContext;
    private readonly IEventPublisher _publisher;

    public LibraryEventService(
        AkiraDbContext dbContext,
        IEventPublisher publisher)
    {
        _dbContext = dbContext;
        _publisher = publisher;
    }

    /// <inheritdoc />
    public Task PublishSyncAsync(LibrarySyncReport report, ScanKind kind, CancellationToken cancellationToken) =>
        _publisher.PublishAsync(FromReport(report, kind));

    /// <inheritdoc />
    public async Task PublishProbeAsync(ProbeRunResult probe, RescanResult? rescan, CancellationToken cancellationToken)
    {
        if (probe.Probed + probe.Missing + probe.Failed == 0 && rescan?.ChangedItems.Count is null or 0)
        {
            return;
        }

        var ids = probe.ProbedItems.Concat(rescan?.ChangedItems ?? []).Distinct().ToList();
        var items = new List<ItemChange>();
        foreach (var chunk in ids.Chunk(500))
        {
            var changes = await _dbContext.MediaItems.AsNoTracking()
                .Where(item => chunk.Contains(item.Id))
                .Select(ItemChange.FromItem)
                .ToListAsync(cancellationToken);
            items.AddRange(changes);
        }

        var events = items.Select(item => new EventDraft(EventTypes.ItemUpdated, Payload(item))).ToList();
        var status = probe.Failed > 0 ? ScanStatus.PartiallySucceeded : ScanStatus.Succeeded;
        var summary = new ScanProgressPayload(ScanKind.Probe, status, [], 0, probe.Probed, probe.Missing, probe.Failed);
        events.Add(new EventDraft(EventTypes.ScanProgress, summary));
        await _publisher.PublishAsync(events);
    }

    /// <summary>
    /// Turns a sync into one event per changed item, followed by a summary.
    /// </summary>
    public static IReadOnlyList<EventDraft> FromReport(LibrarySyncReport report, ScanKind kind)
    {
        // One event per item and run. An item one instance added and another updated is added,
        // one that is gone by the end is removed, whatever happened to it before.
        var removed = report.Removed.Select(change => change.ItemId).ToHashSet();
        var announced = new HashSet<Guid>(removed);
        var events = new List<EventDraft>();

        var added = report.Instances
            .SelectMany(result => result.AddedItems)
            .Where(change => announced.Add(change.ItemId));
        foreach (var change in added)
        {
            events.Add(new EventDraft(EventTypes.ItemAdded, Payload(change)));
        }

        var updated = report.Instances
            .SelectMany(result => result.UpdatedItems.Concat(result.UnlinkedItems))
            .Where(change => announced.Add(change.ItemId));
        foreach (var change in updated)
        {
            events.Add(new EventDraft(EventTypes.ItemUpdated, Payload(change)));
        }

        var replacements = report.Instances
            .SelectMany(result => result.ReplacedFiles)
            .Where(r => !removed.Contains(r.ItemId));
        foreach (var replaced in replacements)
        {
            var payload = new FileReplacedPayload(replaced.ItemId, replaced.InstanceKey, replaced.PreviousFileId, replaced.FileId);
            events.Add(new EventDraft(EventTypes.FileReplaced, payload));
        }

        events.AddRange(report.Removed.Select(change => new EventDraft(EventTypes.ItemRemoved, Payload(change))));

        var instances = report.Instances
            .Select(result => new ScanInstance(result.InstanceKey, Status(result.Status), result.Added, result.Updated, result.Unlinked))
            .ToList();
        var status = instances.Count == 0 ? ScanStatus.Succeeded : instances.Max(instance => instance.Status);
        var summary = new ScanProgressPayload(kind, status, instances, report.RemovedItems, 0, 0, 0);
        events.Add(new EventDraft(EventTypes.ScanProgress, summary));
        return events;
    }

    private static LibraryItemPayload Payload(ItemChange change) =>
        new(change.ItemId, change.Type, change.IdentityKey, change.ParentId, change.SeriesId);

    private static ScanStatus Status(SyncStatus status) => status switch
    {
        SyncStatus.Failed => ScanStatus.Failed,
        SyncStatus.PartiallySucceeded => ScanStatus.PartiallySucceeded,
        _ => ScanStatus.Succeeded
    };
}
