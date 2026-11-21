namespace Akira.Infrastructure.Transcoding;

public interface IHlsTranscoder
{
    /// <summary>
    /// Returns the path of a finished segment, starting or moving ffmpeg when it is not near that segment. Segments well
    /// behind the one asked for are removed.
    /// </summary>
    Task<string> GetSegmentAsync(TranscodeJob job, int index, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the transcode of a session and removes its segments.
    /// </summary>
    void Stop(Guid sessionId);

    /// <summary>
    /// Stops every transcode nobody asked a segment of within the idle timeout.
    /// </summary>
    void StopIdle();

    /// <summary>
    /// Tells how the transcode of a session is going, or null when the session has none.
    /// </summary>
    TranscodeStatus? Status(Guid sessionId);

    // The transcodes running now, HLS and progressive together
    int Running { get; }

    // True once a GPU run failed: until a restart, every transcode uses libx264
    bool SoftwareOnly { get; }

    /// <summary>
    /// Sets SoftwareOnly, for a progressive run that failed on the GPU and then worked with libx264.
    /// </summary>
    void FallBackToSoftware(string reason);

    /// <summary>
    /// Takes one of the Transcoding:MaxConcurrent places, for a progressive run; false when they are all taken.
    /// </summary>
    bool TryReserve();

    /// <summary>
    /// Gives back a place taken with TryReserve.
    /// </summary>
    void Release();
}
