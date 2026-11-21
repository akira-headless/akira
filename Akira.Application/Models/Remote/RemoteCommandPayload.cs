namespace Akira.Server.Models.Remote;

/// <summary>
/// Represents the payload of a remote.command message, a command the device carries out. FromDeviceId
/// is the device that sent it.
/// </summary>
public sealed record RemoteCommandPayload(RemoteCommandType Type, Guid? ItemId, bool FromStart, long? PositionMs, Guid? FromDeviceId);
