namespace Akira.Server.Models.Events;

/// <summary>
/// Represents the outcome of a sync in one instance, with the counts of items it added, updated and unlinked.
/// </summary>
public sealed record ScanInstance(string Instance, ScanStatus Status, int Added, int Updated, int Unlinked);
