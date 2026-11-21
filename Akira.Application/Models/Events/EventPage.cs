namespace Akira.Server.Models.Events;

/// <summary>
/// Represents one page of the events after a sequence. Latest is where to continue from, and Resync is true when the
/// events asked for are gone.
/// </summary>
public sealed record EventPage(IReadOnlyList<EventMessage> Items, long Latest, bool HasMore, bool Resync);
