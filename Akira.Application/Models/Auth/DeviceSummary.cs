using Akira.Core.Enum;

namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a personal or shared device, or a link. UserId is null for a shared device, its users pick a profile.
/// </summary>
public sealed record DeviceSummary(
    Guid Id,
    string Name,
    string Client,
    string? ClientVersion,
    string? Platform,
    DeviceKind Kind,
    Guid? UserId,
    string? UserName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt);
