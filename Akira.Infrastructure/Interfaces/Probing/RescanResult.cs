namespace Akira.Infrastructure.Interfaces.Probing;

/// <summary>
/// Represents what a rescan did. Requeued counts files queued to probe again, ChangedItems are the items whose subtitle
/// files were added or removed.
/// </summary>
public sealed record RescanResult(int Requeued, int SubtitlesAdded, int SubtitlesRemoved, IReadOnlyList<Guid> ChangedItems);
