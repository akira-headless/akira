namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a device waiting to be approved, as an admin sees it in the list.
/// </summary>
public sealed record PendingPairing(string Code, string DeviceName, string Client, string? Platform, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
