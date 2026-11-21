using Akira.Core.Entities;
using Akira.Core.Enum;

namespace Akira.Core.Playback;

/// <summary>
/// Maps ffprobe codecs to the format names clients use in their capabilities.
/// </summary>
public static class Formats
{
    private static readonly HashSet<string> LosslessAudio = new(StringComparer.OrdinalIgnoreCase)
    {
        "truehd", "dtshd", "flac", "alac", "pcm"
    };

    private static readonly HashSet<string> ImageSubtitles = new(StringComparer.OrdinalIgnoreCase) { "pgs", "vobsub", "dvb" };

    /// <summary>
    /// Returns the audio format of a track: ffprobe reports DTS-HD MA as dts with a profile, and every PCM variant separately.
    /// </summary>
    public static string? Audio(MediaStream stream)
    {
        var codec = stream.Codec?.ToLowerInvariant();
        if (codec is null)
        {
            return null;
        }

        if (codec == "dts" && stream.Profile?.Contains("MA", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "dtshd";
        }

        return codec.StartsWith("pcm_", StringComparison.Ordinal) ? "pcm" : codec;
    }

    public static bool IsLossless(string? audioFormat) => audioFormat is not null && LosslessAudio.Contains(audioFormat);

    /// <summary>
    /// Checks whether a subtitle format (as Subtitle names it) is pictures rather than text.
    /// </summary>
    public static bool IsImageSubtitle(string format) => ImageSubtitles.Contains(format);

    public static string? Subtitle(string? codec) => codec?.ToLowerInvariant() switch
    {
        "subrip" or "srt" or "text" => "srt",
        "ass" or "ssa" => "ass",
        "webvtt" => "vtt",
        "mov_text" => "mov_text",
        "hdmv_pgs_subtitle" => "pgs",
        "dvd_subtitle" => "vobsub",
        "dvb_subtitle" => "dvb",
        null => null,
        var other => other
    };

    public static string? Hdr(HdrFormat? format) => format switch
    {
        HdrFormat.Hdr10 => "hdr10",
        HdrFormat.DolbyVision => "dolbyvision",
        HdrFormat.Hlg => "hlg",
        _ => null
    };
}
