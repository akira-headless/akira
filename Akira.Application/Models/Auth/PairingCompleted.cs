namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a device that finished pairing or signed in with a PIN. Token is its device token, handed out only once.
/// </summary>
public sealed record PairingCompleted(string Token, DeviceSummary Device);
