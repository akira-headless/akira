namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to create an API key.
/// </summary>
public sealed record CreateApiKeyRequest(string Name);
