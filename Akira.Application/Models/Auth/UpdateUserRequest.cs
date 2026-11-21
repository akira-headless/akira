using Akira.Core.Enum;

namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to rename a user or change their role. A null field stays as it is.
/// </summary>
public sealed record UpdateUserRequest(string? Name, UserRole? Role);
