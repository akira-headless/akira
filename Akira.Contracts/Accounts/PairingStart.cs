namespace Akira.Contracts.Accounts;

/// <summary>
/// Represents a started pairing: the code someone approves in Akira, and the secret the bridge polls it with.
/// </summary>
public sealed record PairingStart(Guid PairingId, string Code, string PollSecret, DateTimeOffset ExpiresAt);
