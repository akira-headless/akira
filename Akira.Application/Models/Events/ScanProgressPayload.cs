namespace Akira.Server.Models.Events;

/// <summary>
/// Represents the payload of the event sent after a sync, refresh or probe. Instances and Removed are only filled by a
/// sync or refresh, Probed, Missing and Failed only by a probe.
/// </summary>
public sealed record ScanProgressPayload(
    ScanKind Kind,
    ScanStatus Status,
    IReadOnlyList<ScanInstance> Instances,
    int Removed,
    int Probed,
    int Missing,
    int Failed);
