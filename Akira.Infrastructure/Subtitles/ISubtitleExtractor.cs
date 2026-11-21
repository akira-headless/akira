namespace Akira.Infrastructure.Subtitles;

public interface ISubtitleExtractor
{
    /// <summary>
    /// Copies an embedded text subtitle out of a video into its own file.
    /// </summary>
    Task ExtractAsync(string videoPath, int streamIndex, string targetPath, CancellationToken cancellationToken);

    /// <summary>
    /// Copies several embedded text subtitles out of a video, each into its own file, in one read of the video.
    /// </summary>
    Task ExtractAsync(string videoPath, IReadOnlyList<SubtitleExtraction> tracks, CancellationToken cancellationToken);
}
