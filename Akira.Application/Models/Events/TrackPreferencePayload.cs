using Akira.Core.Enum;

namespace Akira.Server.Models.Events;

/// <summary>
/// Represents the payload of the event sent when a user's track preferences changed. IdentityKey is the series or item
/// they belong to, and null for the global ones.
/// </summary>
public sealed record TrackPreferencePayload(PreferenceScope Scope, string? IdentityKey);
