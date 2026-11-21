namespace Akira.Server.Models.Remote;

/// <summary>
/// Represents the body of a request to send a command to a device. Play needs ItemId, FromStart
/// ignores the resume point, and Seek needs PositionMs.
/// </summary>
public sealed record RemoteCommandRequest(RemoteCommandType Type, Guid? ItemId, bool FromStart, long? PositionMs);
