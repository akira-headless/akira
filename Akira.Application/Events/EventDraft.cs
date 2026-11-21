namespace Akira.Server.Events;

/// <summary>
/// Represents an event to publish. Without a UserId the event goes to every client.
/// </summary>
public sealed record EventDraft(string Type, object Payload, Guid? UserId = null, int Version = 1);
