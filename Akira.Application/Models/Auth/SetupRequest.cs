namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to create the first admin. SetupCode is in the server log, and Pin is optional.
/// </summary>
public sealed record SetupRequest(string SetupCode, string Name, string? Pin);
