namespace Akira.Infrastructure.Transcoding;

public interface IKeyframeIndex
{
    /// <summary>
    /// Returns the keyframe times of a file's video in seconds from its start, as ffmpeg counts with -start_at_zero, or
    /// null when the file has no index to read them from (Matroska Cues, an MP4 sample table).
    /// </summary>
    Task<IReadOnlyList<double>?> GetAsync(string path, CancellationToken cancellationToken);
}
