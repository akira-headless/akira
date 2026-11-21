using Akira.Core.Entities;
using Akira.Core.Enum;

namespace Akira.Core.Playback;

/// <summary>
/// Chooses the audio and subtitle track of a file from the user's rules, the item and what the client can play.
/// </summary>
public static class TrackResolver
{
    // A track with at most this share of the events of a full track in the same language is
    // taken for the forced one when nothing is flagged. Forced tracks cover a few signs or lines
    // of foreign dialogue, typically 2 to 15 percent of the full track. Erring low matters: a
    // full track taken for forced shows subtitles all the way through to someone who asked for none.
    public const double ForcedEventShare = 0.25;

    public static TrackSelection Resolve(
        IReadOnlyList<MediaStream> streams,
        string? originalLanguage,
        EffectiveTrackRules rules,
        DeviceCapabilities capabilities)
    {
        var audio = ResolveAudio(streams, originalLanguage, rules, capabilities);
        var audioLanguage = AudioLanguage(streams, audio.Stream, originalLanguage);
        var subtitle = ResolveSubtitle(streams, audioLanguage, rules, capabilities);
        return new TrackSelection(audio, subtitle);
    }

    // MP4 releases tag their audio "und" as a rule. When that is the file's only audio track it is
    // the original language nearly always; with several untagged tracks there is no telling which is which.
    private static string? AudioLanguage(IReadOnlyList<MediaStream> streams, MediaStream? audio, string? originalLanguage)
    {
        if (audio?.Language is { } language)
        {
            return language;
        }

        if (audio is null)
        {
            return null;
        }

        var audioTracks = streams.Count(s => s.Kind == StreamKind.Audio);
        return audioTracks == 1 ? originalLanguage : null;
    }

    private static TrackChoice ResolveAudio(
        IReadOnlyList<MediaStream> streams,
        string? originalLanguage,
        EffectiveTrackRules rules,
        DeviceCapabilities capabilities)
    {
        var audio = streams.Where(s => s.Kind == StreamKind.Audio).OrderBy(s => s.SortOrder).ToList();
        if (audio.Count == 0)
        {
            return new TrackChoice(null, "no-audio");
        }

        // A client that can play none of them may still decode in software, so it gets all of them rather than nothing
        var playable = audio.Where(a => Formats.Audio(a) is { } format && capabilities.CanPlayAudio(format)).ToList();
        if (playable.Count == 0)
        {
            playable = audio;
        }

        // A remembered choice wins over the exclusions: someone who picked the commentary wants the commentary
        if (rules.Audio is { Signature: { } signature } remembered)
        {
            var scope = Scope(remembered);
            if (playable.FirstOrDefault(a => TrackSignatures.For(a) == signature) is { } exact)
            {
                return new TrackChoice(exact, $"remembered-{scope}");
            }

            var languageAndFlags = TrackSignatures.LanguageAndFlags(signature);
            if (BestAudio(playable.Where(a => TrackSignatures.LanguageAndFlags(a) == languageAndFlags), rules, capabilities) is { } similar)
            {
                return new TrackChoice(similar, $"remembered-{scope}-language");
            }
        }

        var candidates = playable
            .Where(a => !(rules.ExcludeCommentary && a.IsCommentary) && !(rules.ExcludeDescriptive && a.IsDescriptive))
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = playable;
        }

        if (rules.PreferOriginalLanguage && originalLanguage is not null
            && BestAudio(candidates.Where(a => a.Language == originalLanguage), rules, capabilities) is { } original)
        {
            return new TrackChoice(original, "original-language");
        }

        foreach (var language in rules.AudioLanguages)
        {
            if (BestAudio(candidates.Where(a => a.Language == language), rules, capabilities) is { } preferred)
            {
                return new TrackChoice(preferred, "preferred-language");
            }
        }

        return candidates.FirstOrDefault(a => a.IsDefault) is { } byDefault
            ? new TrackChoice(byDefault, "default-track")
            : new TrackChoice(candidates[0], "first-track");
    }

    private static MediaStream? BestAudio(IEnumerable<MediaStream> tracks, EffectiveTrackRules rules, DeviceCapabilities capabilities) =>
        tracks
            .OrderByDescending(a => rules.PreferLossless && IsUsableLossless(a, capabilities))
            .ThenByDescending(a => a.Channels ?? 0)
            .ThenByDescending(a => a.IsDefault)
            .ThenBy(a => a.SortOrder)
            .FirstOrDefault();

    // Lossless only helps when it reaches the receiver untouched, or is decoded locally (FLAC, PCM).
    // A TrueHD track a client can only decode is no better than its lossy companion.
    private static bool IsUsableLossless(MediaStream audio, DeviceCapabilities capabilities)
    {
        var format = Formats.Audio(audio);
        if (!Formats.IsLossless(format))
        {
            return false;
        }

        return capabilities.CanPassThrough(format!)
               || (format is "flac" or "pcm" or "alac" && capabilities.CanPlayAudio(format));
    }

    private static TrackChoice ResolveSubtitle(
        IReadOnlyList<MediaStream> streams,
        string? audioLanguage,
        EffectiveTrackRules rules,
        DeviceCapabilities capabilities)
    {
        var subtitles = streams.Where(s => s.Kind == StreamKind.Subtitle).OrderBy(s => s.SortOrder).ToList();
        var supported = subtitles
            .Where(s => Formats.Subtitle(s.Codec) is { } format && capabilities.CanShowSubtitle(format))
            .ToList();

        if (rules.Subtitle is { } remembered)
        {
            var scope = Scope(remembered);
            if (remembered.Off)
            {
                return new TrackChoice(null, $"remembered-{scope}-off");
            }

            if (supported.FirstOrDefault(s => TrackSignatures.For(s) == remembered.Signature) is { } exact)
            {
                return new TrackChoice(exact, $"remembered-{scope}");
            }

            var languageAndFlags = TrackSignatures.LanguageAndFlags(remembered.Signature!);
            if (BestFull(supported.Where(s => TrackSignatures.LanguageAndFlags(s) == languageAndFlags), rules) is { } similar)
            {
                return new TrackChoice(similar, $"remembered-{scope}-language");
            }
        }

        if (rules.SubtitleMode == SubtitleMode.Off)
        {
            return new TrackChoice(null, "mode-off");
        }

        if (supported.Count == 0)
        {
            return new TrackChoice(null, subtitles.Count == 0 ? "no-subtitles" : "no-supported-subtitle");
        }

        switch (rules.SubtitleMode)
        {
            case SubtitleMode.Always:
                return Full(supported, rules.SubtitleLanguages, rules, "always");

            case SubtitleMode.WhenAudioForeign:
                if (audioLanguage is null)
                {
                    return new TrackChoice(null, "audio-language-unknown");
                }

                // "Foreign" means not a language the user reads subtitles in, or failing that one they listen in
                var understood = rules.SubtitleLanguages.Count > 0 ? rules.SubtitleLanguages : rules.AudioLanguages;
                if (understood.Contains(audioLanguage))
                {
                    return Forced(supported, subtitles, audioLanguage);
                }

                return Full(supported, rules.SubtitleLanguages, rules, "audio-foreign");

            default:
                return Forced(supported, subtitles, audioLanguage);
        }
    }

    private static TrackChoice Forced(IReadOnlyList<MediaStream> supported, IReadOnlyList<MediaStream> all, string? audioLanguage)
    {
        if (audioLanguage is null)
        {
            return new TrackChoice(null, "audio-language-unknown");
        }

        var inLanguage = supported.Where(s => s.Language == audioLanguage).ToList();
        if (inLanguage.FirstOrDefault(s => s.IsForced) is { } flagged)
        {
            return new TrackChoice(flagged, "forced");
        }

        if (ForcedByEventCount(inLanguage) is { } counted)
        {
            return new TrackChoice(counted, "forced-heuristic");
        }

        // A forced file without a language, like Movie.forced.srt, belongs to the movie's own language
        if (supported.FirstOrDefault(s => s.IsForced && s.Language is null) is { } unlabeled)
        {
            return new TrackChoice(unlabeled, "forced");
        }

        // Worth telling apart: the foreign dialogue has subtitles, this client just cannot show them
        if (all.Any(s => s.IsForced && (s.Language == audioLanguage || s.Language is null)))
        {
            return new TrackChoice(null, "forced-subtitle-unsupported");
        }

        return new TrackChoice(null, "no-forced-subtitle");
    }

    private static TrackChoice Full(
        IReadOnlyList<MediaStream> supported,
        IReadOnlyList<string> languages,
        EffectiveTrackRules rules,
        string reason)
    {
        foreach (var language in languages)
        {
            var inLanguage = supported.Where(s => s.Language == language).ToList();
            var forced = ForcedByEventCount(inLanguage);
            if (BestFull(inLanguage.Where(s => !s.IsForced && s != forced), rules) is { } full)
            {
                return new TrackChoice(full, reason);
            }
        }

        return new TrackChoice(null, "no-matching-subtitle");
    }

    private static MediaStream? BestFull(IEnumerable<MediaStream> tracks, EffectiveTrackRules rules) =>
        tracks
            .OrderByDescending(s => s.IsHearingImpaired == rules.PreferHearingImpaired)
            .ThenByDescending(s => s.IsTextBased)
            .ThenByDescending(s => s.IsDefault)
            .ThenBy(s => s.SortOrder)
            .FirstOrDefault();

    /// <summary>
    /// Finds an unflagged forced track: one with far fewer events than a full track of the same language and kind.
    /// </summary>
    public static MediaStream? ForcedByEventCount(IReadOnlyList<MediaStream> sameLanguage)
    {
        // Image tracks count display sets and text tracks count lines, so each kind is compared with its own
        foreach (var group in sameLanguage
                     .Where(s => !s.IsForced && !s.IsHearingImpaired && s.EventCount > 0)
                     .GroupBy(s => s.IsTextBased))
        {
            var tracks = group.OrderBy(s => s.EventCount).ToList();
            if (tracks.Count >= 2 && tracks[0].EventCount <= tracks[^1].EventCount * ForcedEventShare)
            {
                return tracks[0];
            }
        }

        return null;
    }

    private static string Scope(Remembered remembered) => remembered.Scope == PreferenceScope.Item ? "item" : "series";
}
