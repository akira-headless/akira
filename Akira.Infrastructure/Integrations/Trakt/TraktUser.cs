using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents a Trakt user, by username.
/// </summary>
public sealed record TraktUser([property: JsonPropertyName("username")] string Username);
