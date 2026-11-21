namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Represents a subtitle drawn into the video. An embedded image one is read from the video itself, StreamIndex being
/// ffprobe's index; every other is a file of its own, a text one embedded in the video copied out of it first.
/// </summary>
public sealed record BurnedSubtitle(bool ImageBased, int? StreamIndex, string? FilePath)
{
    public string Key => $"{(ImageBased ? "image" : "text")}:{StreamIndex}:{FilePath}";
}
