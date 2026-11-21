namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a pairing that has started. Code is shown to the user, and the device polls with PollSecret every
/// PollIntervalSeconds until ExpiresAt.
/// </summary>
public sealed record PairingStarted(Guid PairingId, string Code, string PollSecret, DateTimeOffset ExpiresAt, int PollIntervalSeconds);
