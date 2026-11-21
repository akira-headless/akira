namespace Akira.Contracts.Accounts;

/// <summary>
/// Represents the state of a pairing, with the new device's token and caller only once it is approved.
/// </summary>
public sealed record PairingPoll(PairingState State, string? Token, Caller? Caller);
