namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a session for a profile picked on a shared device, with its token.
/// </summary>
public sealed record ProfileSession(string Token, DateTimeOffset ExpiresAt, UserSummary User);
