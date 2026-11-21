namespace Akira.Core.Subtitles;

public static class SubtitleConversion
{
    // Raise when what the readers or writers make changes, so subtitles converted before are converted again
    public const int Version = 3;

    /// <summary>
    /// Reads a subtitle file's cues, whatever encoding it is in. language is the track's, which picks the code page of
    /// a file that is not UTF-8.
    /// </summary>
    public static List<SubtitleCue> Read(ReadOnlySpan<byte> bytes, SubtitleFormat format, string? language = null)
    {
        var text = SubtitleDecoder.Decode(bytes, language);
        return format == SubtitleFormat.Ass ? AssReader.Read(text) : SrtReader.Read(text);
    }

    /// <summary>
    /// Determines the format from a codec name or a file extension; everything that is not ASS or SSA is read as SRT.
    /// </summary>
    public static SubtitleFormat FormatOf(string? codecOrExtension)
    {
        var name = codecOrExtension?.TrimStart('.').ToLowerInvariant();
        return name is "ass" or "ssa" ? SubtitleFormat.Ass : SubtitleFormat.Srt;
    }
}
