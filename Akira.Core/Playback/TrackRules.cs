using Akira.Core.Enum;

namespace Akira.Core.Playback;

/// <summary>
/// Represents the track rules set at one scope. A null field is not set at that scope.
/// </summary>
public sealed record TrackRules(
    IReadOnlyList<string>? AudioLanguages = null,
    bool? PreferOriginalLanguage = null,
    bool? PreferLossless = null,
    bool? ExcludeCommentary = null,
    bool? ExcludeDescriptive = null,
    SubtitleMode? SubtitleMode = null,
    IReadOnlyList<string>? SubtitleLanguages = null,
    bool? PreferHearingImpaired = null,
    string? RememberedAudio = null,
    string? RememberedSubtitle = null,
    bool? RememberedSubtitleOff = null)
{
    public static TrackRules None { get; } = new();
}
