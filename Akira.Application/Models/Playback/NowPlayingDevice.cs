namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the device a playback session plays on.
/// </summary>
public sealed record NowPlayingDevice(Guid Id, string Name, string Client, string? Platform);
