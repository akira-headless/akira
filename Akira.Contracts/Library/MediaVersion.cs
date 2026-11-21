namespace Akira.Contracts.Library;

/// <summary>
/// Represents a file of a movie or episode. Hdr is hdr10, dolbyvision or hlg, and null for SDR or a file not probed
/// yet. ContentType is the type the file is served with.
/// </summary>
public sealed record MediaVersion(
    Guid FileId,
    string Instance,
    string FileName,
    long SizeBytes,
    string? Container,
    long? DurationMs,
    string? VideoCodec,
    int? Width,
    int? Height,
    string? Hdr,
    IReadOnlyList<MediaTrack> Audio,
    IReadOnlyList<MediaTrack> Subtitles,
    string? VideoProfile = null,
    int? BitDepth = null,
    double? FrameRate = null,
    int? VideoOrder = null)
{
    public string ContentType { get; init; } = "application/octet-stream";
}
