using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents a token as Trakt returns it. ExpiresIn is in seconds, CreatedAt in Unix seconds.
/// </summary>
public sealed record TraktToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("created_at")] long CreatedAt);
