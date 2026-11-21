namespace Akira.Infrastructure.Subtitles;

/// <summary>
/// Represents an embedded subtitle to copy out of a video: ffprobe's index of the stream and the file it goes to.
/// </summary>
public sealed record SubtitleExtraction(int StreamIndex, string TargetPath);
