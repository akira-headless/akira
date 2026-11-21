using System.Globalization;
using Akira.Core.Entities;

namespace Akira.Core.Playback;

/// <summary>
/// Represents what has to change about a file for a client to play it. Reasons is empty for direct play.
/// </summary>
public sealed record StreamDecision(
    bool CopyVideo,
    AudioOutput Audio,
    int? Height,
    bool ToneMap,
    IReadOnlyList<string> Reasons,
    // The subtitle formats the client shows itself, null for every format; anything else is burned into the video
    IReadOnlyList<string>? ShownSubtitles = null,
    // The most the client takes, in kbit/s, for the video and audio together; null leaves the server's own limit
    int? MaxBitrateKbps = null,
    // Whether the client plays AC-3, so another surround track can stay surround
    bool Surround = false)
{
    public const string ContainerReason = "container";
    public const string VideoCodecReason = "video-codec";
    public const string HdrReason = "hdr";
    public const string ResolutionReason = "resolution";
    public const string AudioCodecReason = "audio-codec";
    public const string AccessModeReason = "access-mode";
    public const string SubtitleReason = "subtitle";
    public const string BitrateReason = "bitrate";

    // What an MPEG-TS segment can carry as it is
    private static readonly HashSet<string> SegmentVideo = new(StringComparer.OrdinalIgnoreCase) { "h264", "hevc" };
    private static readonly HashSet<string> SegmentAudio = new(StringComparer.OrdinalIgnoreCase) { "aac", "ac3", "eac3", "mp3" };

    public bool DirectPlay => Reasons.Count == 0;

    /// <summary>
    /// Decides what has to change for a client. hlsOnly is a client that opens nothing but HLS, so even a file it plays
    /// goes out as segments.
    /// </summary>
    public static StreamDecision Decide(MediaFile file, MediaStream? video, MediaStream? audio, DeviceCapabilities capabilities, bool hlsOnly = false,
        MediaStream? subtitle = null)
    {
        var reasons = new List<string>();
        var shown = capabilities.SubtitleFormats;
        if (!capabilities.CanOpenContainer(file.Container))
        {
            reasons.Add(ContainerReason);
        }

        var encodeVideo = false;
        var toneMap = false;
        int? height = null;
        if (video is not null)
        {
            if (!capabilities.CanPlayVideo(video.Codec))
            {
                reasons.Add(VideoCodecReason);
                encodeVideo = true;
            }

            if (Formats.Hdr(video.HdrFormat) is { } hdr && !capabilities.CanShowHdr(hdr))
            {
                reasons.Add(HdrReason);
                encodeVideo = toneMap = true;
            }

            if (capabilities.MaxHeight is { } max && video.Height > max)
            {
                reasons.Add(ResolutionReason);
                encodeVideo = true;
                height = max;
            }

            // The whole file over what the client's connection takes, such as a 4K remux away from home
            if (capabilities.MaxBitrateKbps is { } cap && Kbps(file) is { } kbps && kbps > cap)
            {
                reasons.Add(BitrateReason);
                encodeVideo = true;
            }
        }

        var audioOutput = AudioOutput.Copy;
        if (audio is not null && Formats.Audio(audio) is { } format && !capabilities.CanPlayAudio(format))
        {
            reasons.Add(AudioCodecReason);
            audioOutput = Encoded(audio, capabilities);
        }

        // Segments carry no subtitles, text goes beside them as WebVTT only: once the stream is HLS, an image subtitle, or
        // text for a client without WebVTT, has to be burned in
        var shownInStream = reasons.Count > 0 || hlsOnly ? InHls(shown) : shown;
        if (video is not null && NeedsBurning(subtitle, shownInStream))
        {
            reasons.Add(SubtitleReason);
            encodeVideo = true;
        }

        if (reasons.Count == 0 && hlsOnly)
        {
            reasons.Add(AccessModeReason);
        }

        if (reasons.Count == 0)
        {
            return new StreamDecision(true, AudioOutput.Copy, null, false, reasons);
        }

        // Once it goes through HLS the segments have to carry what is copied
        if (video is not null && !encodeVideo && !SegmentVideo.Contains(video.Codec ?? ""))
        {
            encodeVideo = true;
        }

        // Copied audio cannot be cut where a seek starts, it would begin seconds before the encoded video and move every
        // segment boundary; next to a video encode, an audio encode costs nothing
        if (audio is not null && audioOutput == AudioOutput.Copy && (encodeVideo || !SegmentAudio.Contains(Formats.Audio(audio) ?? "")))
        {
            audioOutput = Encoded(audio, capabilities);
        }

        // A low cap is better spent on fewer pixels, which a CPU also encodes in time
        if (encodeVideo && video?.Height is { } source && capabilities.MaxBitrateKbps is { } rate && HeightFor(rate) is { } limit && source > limit)
        {
            height = Math.Min(height ?? limit, limit);
        }

        // The client's cap stays with the session, for a remux that becomes an encode when a subtitle is burned in later
        return new StreamDecision(!encodeVideo, audioOutput, height, toneMap, reasons, InHls(shown), capabilities.MaxBitrateKbps,
            capabilities.CanPlayAudio("ac3"));
    }

    // What an HLS stream shows of the client's subtitles: text as WebVTT beside it, when the client shows WebVTT
    private static IReadOnlyList<string> InHls(IReadOnlyList<string>? shown) =>
        shown is null || shown.Contains("vtt", StringComparer.OrdinalIgnoreCase) ? ["vtt"] : [];

    private static int? HeightFor(int kbps) => kbps switch
    {
        <= 2_500 => 480,
        <= 5_000 => 720,
        <= 10_000 => 1080,
        _ => null
    };

    // The file's own rate, video and audio together, from its size and length
    internal static long? Kbps(MediaFile file) => file.DurationMs is > 0 ? file.SizeBytes * 8 / file.DurationMs.Value : null;

    /// <summary>
    /// Checks whether a subtitle has to be burned into the video for this client.
    /// </summary>
    public bool Burns(MediaStream? subtitle) => NeedsBurning(subtitle, ShownSubtitles);

    /// <summary>
    /// Keeps the decision for another subtitle: one the client cannot show makes a copied video an encode, so it can be
    /// burned in.
    /// </summary>
    public StreamDecision WithSubtitle(MediaStream? next)
    {
        if (!Burns(next) || !CopyVideo)
        {
            return this;
        }

        var audio = Audio == AudioOutput.Copy ? AudioOutput.Aac : Audio;
        IReadOnlyList<string> reasons = Reasons.Contains(SubtitleReason) ? Reasons : [.. Reasons, SubtitleReason];
        return this with { CopyVideo = false, Audio = audio, Reasons = reasons };
    }

    private static bool NeedsBurning(MediaStream? subtitle, IReadOnlyList<string>? shown)
    {
        if (subtitle is null || shown is null)
        {
            return false;
        }

        var format = Formats.Subtitle(subtitle.Codec);
        // A text subtitle reaches a client that shows WebVTT as the converted sidecar, which keeps it switchable
        return !(format is not null && shown.Contains(format, StringComparer.OrdinalIgnoreCase))
               && !(subtitle.IsTextBased && shown.Contains("vtt", StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Keeps the decision for another audio track of the same file: an encode stays an encode, a copy only stays one
    /// for the same codec.
    /// </summary>
    public StreamDecision WithAudio(MediaStream? previous, MediaStream? next)
    {
        if (next is null)
        {
            return this;
        }

        if (Audio == AudioOutput.Copy && Formats.Audio(next) == PreviousFormat(previous))
        {
            return this;
        }

        // Surround stays surround for a client that plays AC-3, which an AC-3 encode already shows it does
        var encoded = next.Channels > 2 && (Surround || Audio == AudioOutput.Ac3) ? AudioOutput.Ac3 : AudioOutput.Aac;
        return encoded == Audio ? this : this with { Audio = encoded };
    }

    private static string? PreviousFormat(MediaStream? previous) => previous is null ? null : Formats.Audio(previous);

    // Surround stays surround as AC-3 for a client that plays it, everything else becomes stereo AAC
    private static AudioOutput Encoded(MediaStream audio, DeviceCapabilities capabilities) =>
        audio.Channels > 2 && capabilities.CanPlayAudio("ac3") ? AudioOutput.Ac3 : AudioOutput.Aac;

    /// <summary>
    /// Writes the decision as the short text a playback session stores.
    /// </summary>
    public string ToToken()
    {
        var subs = ShownSubtitles is null ? "*" : string.Join(',', ShownSubtitles);
        return string.Join(';',
            "video=" + (CopyVideo ? "copy" : "h264"),
            "audio=" + Audio.ToString().ToLowerInvariant(),
            "height=" + (Height?.ToString(CultureInfo.InvariantCulture) ?? ""),
            "tonemap=" + (ToneMap ? "1" : "0"),
            "reasons=" + string.Join(',', Reasons),
            "subs=" + subs,
            "bitrate=" + (MaxBitrateKbps?.ToString(CultureInfo.InvariantCulture) ?? ""),
            "surround=" + (Surround ? "1" : "0"));
    }

    public static StreamDecision? FromToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        // The first of a name counts, so a stored token that repeats one still reads
        var parts = token.Split(';').Select(part => part.Split('=', 2)).Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0]).ToDictionary(group => group.Key, group => group.First()[1]);
        if (!parts.TryGetValue("video", out var video) || !parts.TryGetValue("audio", out var audio)
            || !System.Enum.TryParse<AudioOutput>(audio, true, out var audioOutput))
        {
            return null;
        }

        int? height = int.TryParse(parts.GetValueOrDefault("height"), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        var reasons = (parts.GetValueOrDefault("reasons") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var subs = parts.GetValueOrDefault("subs");
        string[]? shownSubtitles = subs is null or "*" ? null : subs.Split(',', StringSplitOptions.RemoveEmptyEntries);
        int? bitrate = int.TryParse(parts.GetValueOrDefault("bitrate"), NumberStyles.None, CultureInfo.InvariantCulture, out var kbps) ? kbps : null;
        var toneMap = parts.GetValueOrDefault("tonemap") == "1";
        var surround = parts.GetValueOrDefault("surround") == "1";
        return new StreamDecision(video == "copy", audioOutput, height, toneMap, reasons, shownSubtitles, bitrate, surround);
    }
}
