namespace Akira.Infrastructure.Transcoding;

public interface IProgressiveTranscoder
{
    /// <summary>
    /// Writes a version as one MPEG-TS stream, from a time onwards, into output until it ends or the client goes away.
    /// </summary>
    Task StreamAsync(TranscodeJob job, double startSeconds, Stream output, CancellationToken cancellationToken);
}
