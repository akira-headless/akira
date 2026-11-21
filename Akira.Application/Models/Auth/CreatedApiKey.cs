namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a new API key. Key is only in this response and never shown again.
/// </summary>
public sealed record CreatedApiKey(ApiKeySummary Summary, string Key);
