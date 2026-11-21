using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Represents Trakt's user settings of the signed in account, read only for its username.
/// </summary>
public sealed record TraktUserSettings([property: JsonPropertyName("user")] TraktUser User);
