using Akira.Core.Enum;

namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the track rules that apply once the item, series, global and server rules are combined.
/// </summary>
public sealed record EffectiveRulesModel(
    IReadOnlyList<string> AudioLanguages,
    bool PreferOriginalLanguage,
    bool PreferLossless,
    bool ExcludeCommentary,
    bool ExcludeDescriptive,
    SubtitleMode SubtitleMode,
    IReadOnlyList<string> SubtitleLanguages,
    bool PreferHearingImpaired);
