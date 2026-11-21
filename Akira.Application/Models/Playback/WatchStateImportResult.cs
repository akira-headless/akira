namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the result of a watch state import. Unmatched holds the places in the request of the
/// entries Akira could not key on.
/// </summary>
public sealed record WatchStateImportResult(int Received, int Updated, int Unchanged, IReadOnlyList<int> Unmatched, bool DryRun);
