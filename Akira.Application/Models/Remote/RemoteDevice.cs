namespace Akira.Server.Models.Remote;

/// <summary>
/// Represents one of the caller's devices that can be controlled now, with what it plays.
/// </summary>
public sealed record RemoteDevice(Guid Id, string Name, string Client, string? Platform, RemoteNowPlaying? NowPlaying);
