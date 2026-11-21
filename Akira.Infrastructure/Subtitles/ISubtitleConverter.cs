namespace Akira.Infrastructure.Subtitles;

public interface ISubtitleConverter
{
    /// <summary>
    /// Converts a text subtitle to WebVTT, or reuses the conversion while the source is unchanged, and returns its path.
    /// sameVideo are the other text subtitles embedded in the same video, converted in the same read of it.
    /// </summary>
    Task<string> WebVttAsync(SubtitleSource source, IReadOnlyList<SubtitleSource> sameVideo, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a file of a text subtitle that ffmpeg's subtitles filter reads in full: a repaired UTF-8 copy of an SRT,
    /// a UTF-8 copy of an ASS in another encoding, an embedded ASS with the video's fonts, or the file itself.
    /// sameVideo are the other text subtitles embedded in the same video, copied out in the same read of it.
    /// </summary>
    Task<string> BurnableAsync(SubtitleSource source, IReadOnlyList<SubtitleSource> sameVideo, CancellationToken cancellationToken);
}
