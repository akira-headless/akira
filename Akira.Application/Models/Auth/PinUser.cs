namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a user who can sign in with their PIN.
/// </summary>
public sealed record PinUser(Guid Id, string Name);
