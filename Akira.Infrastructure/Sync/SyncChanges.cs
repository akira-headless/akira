using Microsoft.EntityFrameworkCore;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;

namespace Akira.Infrastructure.Sync;

/// <summary>
/// Collects what a sync changed, a save at a time, from the change tracker just before each one.
/// </summary>
internal sealed class SyncChanges
{
    private readonly string _instanceKey;
    private readonly Dictionary<Guid, ItemChange> _added = [];
    private readonly Dictionary<Guid, ItemChange> _updated = [];
    private readonly Dictionary<Guid, ItemChange> _unlinked = [];
    private readonly List<FileReplacement> _replaced = [];
    private readonly List<string> _problems = [];

    public SyncChanges(string instanceKey)
    {
        _instanceKey = instanceKey;
    }

    public void Problem(string problem)
    {
        // Enough to diagnose a failing Sonarr without one line per series of a large library. A batch that is
        // tried again says the same thing twice.
        if (_problems.Count < 50 && !_problems.Contains(problem))
        {
            _problems.Add(problem);
        }
    }

    /// <summary>
    /// Saves what is tracked and keeps what that changed for the result. A save that fails leaves the result as it was.
    /// </summary>
    public async Task SaveAsync(AkiraDbContext dbContext, IReadOnlyCollection<MediaItem> unlinked, CancellationToken cancellationToken)
    {
        var entries = dbContext.ChangeTracker.Entries().ToList();
        var items = entries
            .Where(entry => entry.Entity is MediaItem)
            .Select(entry => (MediaItem)entry.Entity)
            .ToDictionary(item => item.Id);

        var added = entries
            .Where(entry => entry is { Entity: MediaItem, State: EntityState.Added })
            .Select(entry => ((MediaItem)entry.Entity).Id)
            .ToHashSet();
        var gone = unlinked.Select(item => item.Id).ToHashSet();

        var touched = entries
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity switch
            {
                MediaItem item => item.Id,
                ArrLink link => link.MediaItemId,
                MediaFile file => file.MediaItemId,
                MediaImage image => image.MediaItemId,
                _ => Guid.Empty
            })
            .Where(id => id != Guid.Empty && !added.Contains(id) && !gone.Contains(id) && items.ContainsKey(id))
            .ToHashSet();

        // A version that was deleted and added in one sync is an upgrade or a rename
        var files = entries.Where(entry => entry.Entity is MediaFile).ToList();
        var deletedFiles = files
            .Where(entry => entry.State == EntityState.Deleted)
            .Select(entry => (MediaFile)entry.Entity);
        var addedFiles = files
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => (MediaFile)entry.Entity);
        var replaced = deletedFiles
            .Join(addedFiles,
                previous => (previous.MediaItemId, previous.InstanceKey),
                current => (current.MediaItemId, current.InstanceKey),
                (previous, current) => new FileReplacement(current.MediaItemId, current.InstanceKey, previous.Id, current.Id))
            .ToList();

        // Described now: the save lets go of what it deleted
        var addedChanges = added.Select(id => Describe(items[id])).ToList();
        var touchedChanges = touched.Select(id => Describe(items[id])).ToList();
        var unlinkedChanges = unlinked.Select(Describe).ToList();

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var change in addedChanges)
        {
            _added[change.ItemId] = change;
        }

        foreach (var change in touchedChanges.Where(change => !_added.ContainsKey(change.ItemId)))
        {
            _updated[change.ItemId] = change;
        }

        foreach (var change in unlinkedChanges)
        {
            _updated.Remove(change.ItemId);
            _unlinked[change.ItemId] = change;
        }

        _replaced.AddRange(replaced);
    }

    public SyncResult ToResult()
    {
        var status = _problems.Count > 0 ? SyncStatus.PartiallySucceeded : SyncStatus.Succeeded;
        return new SyncResult(_instanceKey, status, _added.Values.ToList(), _updated.Values.ToList(), _unlinked.Values.ToList(),
            _replaced.ToList(), _problems.ToList());
    }

    public static ItemChange Describe(MediaItem item)
    {
        Guid? seriesId = item.Type switch
        {
            MediaItemType.Series => item.Id,
            MediaItemType.Season => item.ParentId,
            MediaItemType.Episode => item.Parent?.ParentId,
            _ => null
        };
        return new ItemChange(item.Id, item.Type, item.IdentityKey, item.ParentId, seriesId);
    }
}
