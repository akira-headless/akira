using System.Security.Claims;
using Akira.Contracts.Playback;

namespace Akira.Server.Bridges;

public interface IProgressiveService
{
    /// <summary>
    /// Says what the progressive urls of these files send the user: the file as it is when every renderer plays it and
    /// nothing has to be burned in, otherwise a transcode, with the type each goes out as. Makes no session.
    /// </summary>
    Task<IReadOnlyList<ProgressiveStream>> DescribeAsync(ClaimsPrincipal user, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken);

    /// <summary>
    /// Says what a progressive url sends, the same as a GET would, or null when the file or the user is gone.
    /// </summary>
    Task<ProgressiveStream?> DescribeAsync(SignedTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Plans the playback behind a progressive url, for its user and with their track rules, or returns null when the
    /// file or the user is gone. The session is not started. A file that goes out as it is joins the viewing its earlier
    /// ranges began.
    /// </summary>
    Task<ProgressivePlan?> PlanAsync(SignedTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the planned transcode into output from a time onwards and ends its session however the stream ends: at
    /// where the renderer got to when it stayed to watch, without a position when it only looked or nothing could be
    /// sent. Throws TranscodeException when ffmpeg wrote nothing.
    /// </summary>
    Task StreamAsync(ProgressivePlan plan, double startSeconds, Stream output, CancellationToken cancellationToken);

    /// <summary>
    /// Runs send, which writes the file from fromByte into output, as one request of the plan's viewing: the session
    /// starts once a request has streamed ten seconds, and the viewing ends where the ranges got to after the last one.
    /// </summary>
    Task SendAsIsAsync(ProgressivePlan plan, long fromByte, CountingStream output, Func<Task> send, CancellationToken cancellationToken);

    /// <summary>
    /// Ends a planned request that sent nothing: a transcode's session ends without a position, a file's request leaves
    /// its viewing as it was.
    /// </summary>
    Task EndAsync(ProgressivePlan plan, CancellationToken cancellationToken);
}
