namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents the outcome of marking an item played or unplayed. Items counts what was marked, the episodes below a
/// season or series included.
/// </summary>
public sealed record MarkPlayedResult(Guid AuditId, bool Played, int Items);
