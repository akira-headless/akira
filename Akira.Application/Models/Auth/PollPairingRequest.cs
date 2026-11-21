namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a poll for a pairing's token. PollSecret is the one the pairing started with.
/// </summary>
public sealed record PollPairingRequest(string PollSecret);
