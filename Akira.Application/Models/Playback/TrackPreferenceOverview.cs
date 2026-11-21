namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents a user's track rules. Global is the user's own, ServerDefault the household's from the configuration,
/// Effective what applies when neither an item nor a series says otherwise.
/// </summary>
public sealed record TrackPreferenceOverview(
    TrackRulesModel Global,
    IReadOnlyList<TrackPreferenceOverride> Overrides,
    TrackRulesModel ServerDefault,
    EffectiveRulesModel Effective);
