namespace Akira.Contracts.Playback;

/// <summary>
/// Represents what a client plays, in Akira's names: codecs as ffprobe calls them (h264, hevc, av1, aac, ac3, eac3,
/// dts, truehd), Hdr as hdr10, hlg or dolbyvision, and subtitle formats as file types (srt, ass, vtt, pgs). A list left
/// null means the client takes anything. Hls is true when the client plays an HLS transcode of a file that does not
/// suit it.
/// </summary>
public sealed record ClientCapabilities(
    IReadOnlyList<string>? VideoCodecs,
    IReadOnlyList<string> Hdr,
    IReadOnlyList<string>? AudioCodecs,
    IReadOnlyList<string> AudioPassthrough,
    IReadOnlyList<string> SubtitleFormats,
    IReadOnlyList<string>? Containers,
    bool Hls)
{
    public int? MaxHeight { get; init; }
}
