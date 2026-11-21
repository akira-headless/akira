namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents an API key without the key itself. Prefix is the start of the key, to tell keys apart.
/// </summary>
public sealed record ApiKeySummary(Guid Id, string Name, string Prefix, Guid UserId, string UserName, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
