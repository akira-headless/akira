using Akira.Core.Enum;

namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the track rules of a movie, episode or series and what applies. RemembersAudio and
/// RemembersSubtitle are true when a track the user picked during playback is remembered.
/// </summary>
public sealed record ItemTrackPreferences(
    PreferenceScope Scope,
    TrackRulesModel Rules,
    EffectiveRulesModel Effective,
    bool RemembersAudio,
    bool RemembersSubtitle);
