namespace Akira.Contracts.Events;

/// <summary>
/// Represents a page of events after a sequence. Next is the sequence to ask after the next time, also when Events is empty.
/// </summary>
public sealed record EventBatch(IReadOnlyList<AkiraEvent> Events, long Next);
