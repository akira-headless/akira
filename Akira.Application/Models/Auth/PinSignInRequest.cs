namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to sign a device in as a user who gives their PIN. The other fields describe the
/// new personal device.
/// </summary>
public sealed record PinSignInRequest(Guid UserId, string? Pin, string DeviceName, string Client, string? ClientVersion, string? Platform);
