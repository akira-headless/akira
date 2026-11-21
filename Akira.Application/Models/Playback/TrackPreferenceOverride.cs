using Akira.Core.Enum;

namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the rules or remembered tracks a user has for one series or item. ItemId and Title are
/// null when the library does not have it.
/// </summary>
public sealed record TrackPreferenceOverride(
    PreferenceScope Scope,
    Guid? ItemId,
    string? Title,
    TrackRulesModel Rules,
    bool RemembersAudio,
    bool RemembersSubtitle);
