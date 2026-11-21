namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the body of a request to end a playback session, with the last position.
/// </summary>
public sealed record StopRequest(long PositionMs);
