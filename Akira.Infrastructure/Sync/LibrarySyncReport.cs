namespace Akira.Infrastructure.Sync;

/// <summary>
/// Represents a sync of the library. Instances has a result per instance, Removed the items no instance links any more.
/// </summary>
public sealed record LibrarySyncReport(IReadOnlyList<SyncResult> Instances, IReadOnlyList<ItemChange> Removed)
{
    public int RemovedItems => Removed.Count;
}
