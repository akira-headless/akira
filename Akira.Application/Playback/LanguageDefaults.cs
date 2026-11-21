using Akira.Core.Enum;
using Akira.Core.Media;
using Akira.Core.Playback;

namespace Akira.Server.Playback;

public sealed class LanguageDefaults
{
    // Comma separated ISO 639-1 or 639-2 codes in order of preference, so one environment variable holds the list
    public string? Subtitles { get; set; }
    public string? Audio { get; set; }
    public SubtitleMode? SubtitleMode { get; set; }
    public bool? PreferOriginalLanguage { get; set; }

    public TrackRules ToRules() => new(
        AudioLanguages: Parse(Audio),
        PreferOriginalLanguage: PreferOriginalLanguage,
        SubtitleMode: SubtitleMode,
        SubtitleLanguages: Parse(Subtitles));

    /// <summary>
    /// Reads a comma separated list into stored codes, or null when it is empty. Unknown codes are dropped; the validator names them.
    /// </summary>
    public static IReadOnlyList<string>? Parse(string? list)
    {
        var codes = Split(list).Select(Languages.Normalize).OfType<string>().Distinct().ToList();
        return codes.Count == 0 ? null : codes;
    }

    public static IEnumerable<string> Split(string? list) =>
        (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
