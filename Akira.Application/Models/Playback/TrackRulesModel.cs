using Akira.Core.Enum;

namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents a set of track rules. A field left null falls back to the series, global or server rules.
/// </summary>
public sealed record TrackRulesModel(
    IReadOnlyList<string>? AudioLanguages,
    bool? PreferOriginalLanguage,
    bool? PreferLossless,
    bool? ExcludeCommentary,
    bool? ExcludeDescriptive,
    SubtitleMode? SubtitleMode,
    IReadOnlyList<string>? SubtitleLanguages,
    bool? PreferHearingImpaired);
