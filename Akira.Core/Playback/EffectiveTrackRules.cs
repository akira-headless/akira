using Akira.Core.Enum;

namespace Akira.Core.Playback;

/// <summary>
/// Represents the track rules that apply to one item, combined from every scope. Audio and Subtitle are the remembered
/// tracks, null when none is.
/// </summary>
public sealed record EffectiveTrackRules(
    IReadOnlyList<string> AudioLanguages,
    bool PreferOriginalLanguage,
    bool PreferLossless,
    bool ExcludeCommentary,
    bool ExcludeDescriptive,
    SubtitleMode SubtitleMode,
    IReadOnlyList<string> SubtitleLanguages,
    bool PreferHearingImpaired,
    Remembered? Audio,
    Remembered? Subtitle)
{
    // Each field comes from the narrowest scope that sets it, so a series can change only its
    // subtitle mode and keep the user's global audio languages. Below the user's own rules is the
    // household default from the server's configuration, below that the built in default.
    public static EffectiveTrackRules Combine(TrackRules item, TrackRules series, TrackRules global, TrackRules? server = null)
    {
        server ??= TrackRules.None;
        var rememberedAudio = Remember(item.RememberedAudio, false, PreferenceScope.Item)
            ?? Remember(series.RememberedAudio, false, PreferenceScope.Series);
        var rememberedSubtitle = Remember(item.RememberedSubtitle, item.RememberedSubtitleOff == true, PreferenceScope.Item)
            ?? Remember(series.RememberedSubtitle, series.RememberedSubtitleOff == true, PreferenceScope.Series);
        return new EffectiveTrackRules(
            item.AudioLanguages ?? series.AudioLanguages ?? global.AudioLanguages ?? server.AudioLanguages ?? [],
            item.PreferOriginalLanguage ?? series.PreferOriginalLanguage ?? global.PreferOriginalLanguage ?? server.PreferOriginalLanguage ?? false,
            item.PreferLossless ?? series.PreferLossless ?? global.PreferLossless ?? server.PreferLossless ?? true,
            item.ExcludeCommentary ?? series.ExcludeCommentary ?? global.ExcludeCommentary ?? server.ExcludeCommentary ?? true,
            item.ExcludeDescriptive ?? series.ExcludeDescriptive ?? global.ExcludeDescriptive ?? server.ExcludeDescriptive ?? true,
            item.SubtitleMode ?? series.SubtitleMode ?? global.SubtitleMode ?? server.SubtitleMode ?? Enum.SubtitleMode.ForcedOnly,
            item.SubtitleLanguages ?? series.SubtitleLanguages ?? global.SubtitleLanguages ?? server.SubtitleLanguages ?? [],
            item.PreferHearingImpaired ?? series.PreferHearingImpaired ?? global.PreferHearingImpaired ?? server.PreferHearingImpaired ?? false,
            rememberedAudio,
            rememberedSubtitle);
    }

    private static Remembered? Remember(string? signature, bool off, PreferenceScope scope)
    {
        if (off)
        {
            return new Remembered(null, true, scope);
        }

        if (signature is null)
        {
            return null;
        }

        return new Remembered(signature, false, scope);
    }
}
