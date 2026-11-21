namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to start pairing a new device, which it describes.
/// </summary>
public sealed record StartPairingRequest(string DeviceName, string Client, string? ClientVersion, string? Platform);
