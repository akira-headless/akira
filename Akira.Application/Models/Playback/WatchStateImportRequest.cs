namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the body of a request to import watch state. With DryRun nothing is changed.
/// </summary>
public sealed record WatchStateImportRequest(IReadOnlyList<WatchStateImportEntry>? Entries, bool DryRun);
