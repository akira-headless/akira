namespace Akira.Server.Models.Events;

/// <summary>
/// Represents the payload of the stream.ready and stream.resync messages of the event socket. Latest is the sequence
/// the client is now at.
/// </summary>
public sealed record StreamPayload(long Latest);
