using Akira.Core.Enum;

namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to create a user. Pin is optional, a user can be created without one.
/// </summary>
public sealed record CreateUserRequest(string Name, UserRole Role, string? Pin);
