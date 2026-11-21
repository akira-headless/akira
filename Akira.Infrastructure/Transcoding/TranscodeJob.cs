using Akira.Core.Playback;

namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Represents what ffmpeg transcodes for a playback session. The indexes are ffprobe's, only ever used to tell ffmpeg
/// which streams to take. Segments null cuts every segment length.
/// </summary>
public sealed record TranscodeJob(
    Guid SessionId,
    string InputPath,
    int? VideoIndex,
    int? AudioIndex,
    int? AudioChannels,
    StreamDecision Decision,
    long DurationMs,
    BurnedSubtitle? Subtitle = null,
    double? FrameRate = null,
    HlsSegments? Segments = null)
{
    // A change of audio track, burned subtitle or decision makes the segments made so far wrong
    public string Key => $"{AudioIndex}|{Subtitle?.Key}|{Decision.ToToken()}";

    public HlsSegments SegmentsFor(TranscodingOptions options) => Segments ?? HlsSegments.Uniform(DurationMs, options.SegmentLength);

    // Only copied video is cut on the file's keyframes; an encode makes its own on every boundary
    public bool CutsOnKeyframes => Decision.CopyVideo && VideoIndex is not null && Segments is { OnKeyframes: true };
}
