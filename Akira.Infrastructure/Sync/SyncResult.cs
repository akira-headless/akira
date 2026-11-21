namespace Akira.Infrastructure.Sync;

/// <summary>
/// Represents the sync of one Radarr or Sonarr instance. UnlinkedItems are the items the instance no longer has.
/// </summary>
public sealed record SyncResult(
    string InstanceKey,
    SyncStatus Status,
    IReadOnlyList<ItemChange> AddedItems,
    IReadOnlyList<ItemChange> UpdatedItems,
    IReadOnlyList<ItemChange> UnlinkedItems,
    IReadOnlyList<FileReplacement> ReplacedFiles,
    IReadOnlyList<string> Problems)
{
    public int Added => AddedItems.Count;
    public int Updated => UpdatedItems.Count;
    public int Unlinked => UnlinkedItems.Count;

    public static SyncResult Failed(string instanceKey, string problem) =>
        new(instanceKey, SyncStatus.Failed, [], [], [], [], [problem]);
}
