namespace Akira.Server.Events;

/// <summary>
/// Represents an event sent to connected clients. A Direct event, such as a remote command, is meant for one connection
/// right now, is never stored and has no sequence.
/// </summary>
public sealed record HubEvent(long Sequence, Guid? UserId, byte[] Json, bool Direct = false);
