using Akira.Core.Enum;

namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a user with their role and whether they have a PIN.
/// </summary>
public sealed record UserSummary(Guid Id, string Name, UserRole Role, bool HasPin);
