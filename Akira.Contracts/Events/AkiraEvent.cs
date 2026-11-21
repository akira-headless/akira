namespace Akira.Contracts.Events;

/// <summary>
/// Represents one change from Akira's event feed: what happened and to which item. Sequence orders the changes.
/// </summary>
public sealed record AkiraEvent(long Sequence, EventKind Kind, Guid ItemId, DateTimeOffset CreatedAt);
