namespace Akira.Core.Media;

/// <summary>
/// Recognizes subtitle files next to a video and reads what their names say about them.
/// </summary>
public static class SubtitleFiles
{
    // .sub is left out: it is either the data half of an .idx pair or a rare MicroDVD file
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".vtt", ".sup", ".idx"
    };

    private static readonly HashSet<string> TextCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text", "microdvd", "subviewer", "subviewer1",
        "sami", "realtext", "stl", "mpl2", "pjs", "vplayer", "jacosub"
    };

    public static bool IsTextBased(string? codec) => codec is not null && TextCodecs.Contains(codec);

    /// <summary>
    /// Parses a file name like "Movie.en.forced.srt" next to "Movie.mkv", or returns null when it is not a subtitle of that video.
    /// </summary>
    public static ExternalSubtitleName? Parse(string videoFileName, string candidateFileName)
    {
        var videoBase = Path.GetFileNameWithoutExtension(videoFileName);
        var extension = Path.GetExtension(candidateFileName);
        if (!Extensions.Contains(extension)
            || !candidateFileName.StartsWith(videoBase + ".", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Movie.srt has nothing between the name and the extension, the dot is shared
        var middleStart = videoBase.Length + 1;
        var middleEnd = candidateFileName.Length - extension.Length;
        var middle = string.Empty;
        if (middleEnd > middleStart)
        {
            middle = candidateFileName[middleStart..middleEnd];
        }

        string? language = null;
        bool forced = false, hearingImpaired = false, isDefault = false;
        var titleWords = new List<string>();

        foreach (var token in middle.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "forced" or "foreign":
                    forced = true;
                    continue;
                case "sdh" or "cc":
                    hearingImpaired = true;
                    continue;
                // ".hi" after a language means hearing impaired, on its own it is Hindi
                case "hi" when language is not null:
                    hearingImpaired = true;
                    continue;
                case "default":
                    isDefault = true;
                    continue;
            }

            // Bazarr and others number duplicates, Movie.eng.0.srt
            if (token.All(char.IsAsciiDigit))
            {
                continue;
            }

            if (language is null && Languages.FromToken(token) is { } code)
            {
                language = code;
                continue;
            }

            titleWords.Add(token);
        }

        var title = titleWords.Count == 0 ? null : string.Join(' ', titleWords);
        return new ExternalSubtitleName(language, forced, hearingImpaired, isDefault, title);
    }
}
