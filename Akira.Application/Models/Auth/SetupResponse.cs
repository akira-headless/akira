namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the first admin that setup created and an API key for it.
/// </summary>
public sealed record SetupResponse(UserSummary User, CreatedApiKey ApiKey);
