namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to deny a waiting pairing code.
/// </summary>
public sealed record DenyPairingRequest(string Code);
