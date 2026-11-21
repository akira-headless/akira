using System.Security.Claims;
using Akira.Contracts.Accounts;
using Akira.Contracts.Playback;
using Microsoft.EntityFrameworkCore;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Transcoding;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Playback;
using Akira.Server.Services;

namespace Akira.Server.Bridges;

public sealed class ProgressiveService : IProgressiveService
{
    private const string StreamContainer = "ts";

    // What a transcode goes out as, from the same table as the files
    public static readonly string ContentType = FileService.VideoContentType(StreamContainer);

    // What every renderer that takes MPEG-TS plays. A file that already is that goes out as it is; anything else is
    // transcoded to it, with the subtitles a user wants burned in, since the stream carries none
    private static readonly CapabilitiesModel Renderer = new(["h264"], [], [], ["aac", "ac3"], [], ["http", "hls"], 1080, [StreamContainer]);

    // A renderer that only wants to know what a stream is reads a moment of it and hangs up, and so does one that seeks
    // on. Only one that stays this long is watching
    private static readonly TimeSpan Watching = TimeSpan.FromSeconds(10);

    private readonly AkiraDbContext _dbContext;
    private readonly IPlaybackService _playback;
    private readonly IHlsService _hls;
    private readonly IProgressiveTranscoder _transcoder;
    private readonly DirectPlays _directPlays;
    private readonly TimeProvider _time;

    public ProgressiveService(
        AkiraDbContext dbContext,
        IPlaybackService playback,
        IHlsService hls,
        IProgressiveTranscoder transcoder,
        DirectPlays directPlays,
        TimeProvider time)
    {
        _dbContext = dbContext;
        _playback = playback;
        _hls = hls;
        _transcoder = transcoder;
        _directPlays = directPlays;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProgressiveStream>> DescribeAsync(ClaimsPrincipal user, IReadOnlyList<Guid> fileIds,
        CancellationToken cancellationToken)
    {
        if (fileIds.Count == 0)
        {
            return [];
        }

        var asIs = await _playback.PlaysAsIsAsync(user, fileIds, Renderer, cancellationToken);
        var files = await _dbContext.MediaFiles.AsNoTracking()
            .Where(file => fileIds.Contains(file.Id))
            .Select(file => new { file.Id, file.Container })
            .ToListAsync(cancellationToken);
        return files
            .Select(file => asIs.Contains(file.Id)
                ? new ProgressiveStream(file.Id, FileService.VideoContentType(file.Container), true)
                : new ProgressiveStream(file.Id, ContentType, false))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ProgressiveStream?> DescribeAsync(SignedTarget target, CancellationToken cancellationToken) =>
        await FindAsync(target, cancellationToken) is { } found
            ? (await DescribeAsync(found.User, [target.Id], cancellationToken)).SingleOrDefault()
            : null;

    /// <inheritdoc />
    public async Task<ProgressivePlan?> PlanAsync(SignedTarget target, CancellationToken cancellationToken)
    {
        if (await FindAsync(target, cancellationToken) is not { } found)
        {
            return null;
        }

        // The ranges after the first are the same viewing, so one viewing is one playback
        var key = (target.Id, found.UserId, target.DeviceId);
        if (_directPlays.Join(key) is { } joined)
        {
            return new ProgressivePlan(found.User, joined.SessionId, target.Id, null, found.DurationMs, joined);
        }

        // Not started: whether this request is someone watching only shows once it has streamed a while
        var result = await _playback.CreatePlanAsync(found.User, found.ItemId, new PlaybackPlanRequest(Renderer, target.Id), cancellationToken);
        if (!result.Succeeded)
        {
            return null;
        }

        var plan = result.Value!;
        if (plan.Transcode is null)
        {
            var play = _directPlays.Add(key, plan.SessionId, found.User, found.SizeBytes, found.DurationMs);
            if (play.SessionId != plan.SessionId)
            {
                await _playback.EndAsync(found.User, plan.SessionId, CancellationToken.None);
            }

            return new ProgressivePlan(found.User, play.SessionId, target.Id, null, found.DurationMs, play);
        }

        if ((await _hls.GetJobAsync(found.User, plan.SessionId, cancellationToken)).Value is not { } job)
        {
            await _playback.EndAsync(found.User, plan.SessionId, CancellationToken.None);
            return null;
        }

        return new ProgressivePlan(found.User, plan.SessionId, target.Id, job, found.DurationMs);
    }

    /// <inheritdoc />
    public async Task StreamAsync(ProgressivePlan plan, double startSeconds, Stream output, CancellationToken cancellationToken)
    {
        var clock = new MpegTsClockStream(output);
        var began = _time.GetTimestamp();
        var started = false;
        try
        {
            var streaming = _transcoder.StreamAsync(plan.Job!, startSeconds, clock, cancellationToken);
            using var waiting = new CancellationTokenSource();
            var watching = Task.Delay(Watching, _time, waiting.Token);
            if (await Task.WhenAny(streaming, watching) == watching)
            {
                started = true;
                await _playback.StartAsync(plan.User, plan.SessionId, CancellationToken.None);
            }

            await waiting.CancelAsync();
            await streaming;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The renderer hung up, which is how most of them stop
        }
        finally
        {
            if (started)
            {
                await _playback.StopAsync(plan.User, plan.SessionId, new StopRequest(StoppedAt(plan, startSeconds, clock, began)), CancellationToken.None);
            }
            else
            {
                await _playback.EndAsync(plan.User, plan.SessionId, CancellationToken.None);
            }
        }
    }

    /// <inheritdoc />
    public async Task SendAsIsAsync(ProgressivePlan plan, long fromByte, CountingStream output, Func<Task> send, CancellationToken cancellationToken)
    {
        var play = plan.Direct!;
        var began = _time.GetTimestamp();
        var watched = false;
        try
        {
            var sending = send();
            using var waiting = new CancellationTokenSource();
            var watching = Task.Delay(Watching, _time, waiting.Token);
            if (await Task.WhenAny(sending, watching) == watching)
            {
                watched = true;
                if (_directPlays.TryStart(play))
                {
                    await _playback.StartAsync(plan.User, plan.SessionId, CancellationToken.None);
                }
            }

            await waiting.CancelAsync();
            await sending;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException && cancellationToken.IsCancellationRequested)
        {
            // The renderer hung up, at the end of a range or to seek
        }
        finally
        {
            // Only a request that streamed a while says where the user is: a probe of a few bytes, or of the end of the file,
            // must not move it
            _directPlays.Leave(play, watched ? ReachedAt(play, fromByte, output.Written, _time.GetElapsedTime(began)) : null);
        }
    }

    /// <inheritdoc />
    public Task EndAsync(ProgressivePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Direct is { } play)
        {
            _directPlays.Leave(play, null);
            return Task.CompletedTask;
        }

        return _playback.EndAsync(plan.User, plan.SessionId, cancellationToken);
    }

    private async Task<Found?> FindAsync(SignedTarget target, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(target.Detail, out var userId))
        {
            return null;
        }

        var file = await _dbContext.MediaFiles
            .AsNoTracking()
            .Where(f => f.Id == target.Id)
            .Select(f => new { f.MediaItemId, f.Path, f.SizeBytes, f.DurationMs })
            .SingleOrDefaultAsync(cancellationToken);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Name, u.Role })
            .SingleOrDefaultAsync(cancellationToken);
        if (file is null || user is null)
        {
            return null;
        }

        // The bytes on disk, which the ranges count in
        var info = new FileInfo(file.Path);
        var caller = new Caller(userId, user.Name, target.DeviceId, user.Role == UserRole.Admin);
        return new Found(BridgePrincipal.For(caller), userId, file.MediaItemId, info.Exists ? info.Length : file.SizeBytes, file.DurationMs ?? 0);
    }

    // A renderer reports nothing, so where it stopped is worked out from the stream. The clock of the last bytes sent is
    // ahead of the picture by what the renderer buffers, and stands still while it is paused, as ffmpeg waits for it to
    // read on. A renderer that reads faster than it plays cannot be further than the time the request took.
    private long StoppedAt(ProgressivePlan plan, double startSeconds, MpegTsClockStream clock, long began)
    {
        var startMs = startSeconds * 1000;
        var sentMs = Math.Max(0, (clock.Seconds ?? startSeconds) * 1000 - startMs);
        var elapsedMs = _time.GetElapsedTime(began).TotalMilliseconds;
        var position = (long)(startMs + Math.Min(sentMs, elapsedMs));
        return Math.Clamp(position, 0, Math.Max(plan.DurationMs, 0));
    }

    // The same for a file sent as it is, from the bytes at the file's average rate: a renderer that buffers is ahead of
    // the picture, one that pauses stops reading
    private static long ReachedAt(DirectPlay play, long fromByte, long sent, TimeSpan elapsed)
    {
        if (play.SizeBytes <= 0 || play.DurationMs <= 0)
        {
            return 0;
        }

        double At(long bytes) => (double)Math.Clamp(bytes, 0, play.SizeBytes) / play.SizeBytes * play.DurationMs;
        var startMs = At(fromByte);
        var position = Math.Min(At(fromByte + sent), startMs + elapsed.TotalMilliseconds);
        return (long)Math.Clamp(position, 0, play.DurationMs);
    }

    /// <summary>
    /// Represents the user and file behind a progressive url.
    /// </summary>
    private sealed record Found(ClaimsPrincipal User, Guid UserId, Guid ItemId, long SizeBytes, long DurationMs);
}
