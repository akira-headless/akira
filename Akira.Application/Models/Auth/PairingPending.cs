using Akira.Core.Enum;

namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a pairing that still waits to be approved, the answer to a poll until then.
/// </summary>
public sealed record PairingPending(PairingStatus Status, DateTimeOffset ExpiresAt);
