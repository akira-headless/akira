using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Server.Authentication;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Subtitles;
using Akira.Infrastructure.Transcoding;
using Akira.Server.Interfaces.Services;
using Akira.Server.Playback;

namespace Akira.Server.Services.Playback;

public class HlsService : IHlsService
{
    private const int AudioBitsPerSecond = 640_000;
    private static readonly TimeSpan BusyRetry = TimeSpan.FromSeconds(10);

    private readonly AkiraDbContext _dbContext;
    private readonly IHlsTranscoder _transcoder;
    private readonly IOptions<TranscodingOptions> _options;
    private readonly ISubtitleConverter _subtitles;
    private readonly IKeyframeIndex _keyframes;
    private readonly HlsStreamCache _streams;

    public HlsService(
        AkiraDbContext dbContext,
        IHlsTranscoder transcoder,
        IOptions<TranscodingOptions> options,
        ISubtitleConverter subtitles,
        IKeyframeIndex keyframes,
        HlsStreamCache streams)
    {
        _dbContext = dbContext;
        _transcoder = transcoder;
        _options = options;
        _subtitles = subtitles;
        _keyframes = keyframes;
        _streams = streams;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<string>> GetMasterPlaylistAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(user, sessionId, cancellationToken);
        if (loaded.Value is not { } stream)
        {
            return ServiceResult<string>.Fail(loaded.Error!.Value, loaded.Detail!);
        }

        var width = stream.Width;
        var height = stream.Height;
        if (stream.Job.Decision.Height is { } target && height is > 0 && width is > 0 && target < height)
        {
            // Scaled like ffmpeg's scale=-2:height, to an even width
            width = (int)Math.Round(width.Value * (double)target / height.Value / 2) * 2;
            height = target;
        }

        // A copy is about as big as the file, an encode at most what libx264 is allowed
        var videoBits = stream.Job.Decision.CopyVideo && stream.Job.DurationMs > 0
            ? stream.SizeBytes * 8 * 1000 / stream.Job.DurationMs
            : FfmpegHlsArguments.VideoBitrateKbps(stream.Job.Decision, _options.Value) * 1000L;
        var playlist = HlsPlaylists.Master(StreamRoutes.HlsMediaName, videoBits + AudioBitsPerSecond, width, height);
        return ServiceResult<string>.Ok(playlist);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<string>> GetMediaPlaylistAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(user, sessionId, cancellationToken);
        if (loaded.Value is not { } stream)
        {
            return ServiceResult<string>.Fail(loaded.Error!.Value, loaded.Detail!);
        }

        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stream.Job.Key)))[..8];
        var playlist = HlsPlaylists.Media(stream.Job.SegmentsFor(_options.Value), index => StreamRoutes.HlsSegmentName(index, version));
        return ServiceResult<string>.Ok(playlist);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<string>> GetSegmentAsync(ClaimsPrincipal user, Guid sessionId, int index, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(user, sessionId, cancellationToken);
        if (loaded.Value is not { } stream)
        {
            return ServiceResult<string>.Fail(loaded.Error!.Value, loaded.Detail!);
        }

        if (index < 0 || index >= stream.Job.SegmentsFor(_options.Value).Count)
        {
            return ServiceResult<string>.Fail(ServiceError.NotFound, $"Segment {index} is not part of this stream.");
        }

        if (!File.Exists(stream.Job.InputPath))
        {
            return ServiceResult<string>.Fail(ServiceError.Conflict, "The file of this session is not on disk where the library says it is.");
        }

        try
        {
            var segment = await _transcoder.GetSegmentAsync(stream.Job, index, cancellationToken);
            return ServiceResult<string>.Ok(segment);
        }
        catch (TranscodeException exception)
        {
            return exception.Failure switch
            {
                TranscodeFailure.OutOfRange => ServiceResult<string>.Fail(ServiceError.NotFound, exception.Message),
                TranscodeFailure.Busy => ServiceResult<string>.Fail(ServiceError.Unavailable, exception.Message, BusyRetry),
                TranscodeFailure.Stopped => ServiceResult<string>.Fail(ServiceError.Gone, exception.Message),
                _ => ServiceResult<string>.Fail(ServiceError.Unavailable, exception.Message)
            };
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TranscodeJob>> GetJobAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(user, sessionId, cancellationToken);
        if (loaded.Value is not { } stream)
        {
            return ServiceResult<TranscodeJob>.Fail(loaded.Error!.Value, loaded.Detail!);
        }

        return ServiceResult<TranscodeJob>.Ok(stream.Job);
    }

    private async Task<ServiceResult<HlsStream>> LoadAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken)
    {
        // Another user's session is answered the same as one that does not exist
        var userId = user.GetUserId();
        var session = await _dbContext.PlaybackSessions.AsNoTracking()
            .Where(s => s.Id == sessionId && s.UserId == userId && s.EndedAt == null)
            .Select(s => new { s.Id, s.MediaFileId, s.AudioStreamId, s.SubtitleStreamId, s.StreamDecision })
            .SingleOrDefaultAsync(cancellationToken);
        if (session is null)
        {
            return ServiceResult<HlsStream>.Fail(ServiceError.NotFound, $"Session {sessionId} does not exist or has ended.");
        }

        if (StreamDecision.FromToken(session.StreamDecision) is not { } decision)
        {
            return ServiceResult<HlsStream>.Fail(ServiceError.NotFound, "This session plays the file itself, it has no HLS stream.");
        }

        // The session is read each time, it says who may ask and whether it ended. The rest only changes with its tracks.
        var tracks = $"{session.MediaFileId}|{session.AudioStreamId}|{session.SubtitleStreamId}|{session.StreamDecision}";
        if (_streams.Find(session.Id, tracks) is { } known)
        {
            return ServiceResult<HlsStream>.Ok(known);
        }

        var file = await _dbContext.MediaFiles.AsNoTracking()
            .Where(f => f.Id == session.MediaFileId)
            .Select(f => new { f.Path, f.DurationMs, f.SizeBytes })
            .SingleAsync(cancellationToken);
        var streams = await _dbContext.MediaStreams.AsNoTracking()
            .Where(s => s.MediaFileId == session.MediaFileId && (s.Kind == StreamKind.Video || s.Kind == StreamKind.Subtitle || s.Id == session.AudioStreamId))
            .OrderBy(s => s.SortOrder)
            .ToListAsync(cancellationToken);
        var video = streams.FirstOrDefault(s => s.Kind == StreamKind.Video);
        var audio = streams.FirstOrDefault(s => s.Id == session.AudioStreamId);

        var subtitle = streams.FirstOrDefault(s => s.Id == session.SubtitleStreamId);
        BurnedSubtitle? burned = null;
        if (decision.Burns(subtitle) && !decision.CopyVideo)
        {
            burned = await BurnAsync(subtitle!, streams, file.Path, cancellationToken);
        }

        // Copied video can only be cut on its own keyframes; a file without an index to read them from is cut every segment length
        HlsSegments? segments = null;
        if (decision.CopyVideo && video is not null && await _keyframes.GetAsync(file.Path, cancellationToken) is { } keyframes)
        {
            segments = HlsSegments.FromKeyframes(keyframes, file.DurationMs ?? 0, _options.Value.SegmentLength);
        }

        var job = new TranscodeJob(session.Id, file.Path, video?.IndexHint, audio?.IndexHint, audio?.Channels, decision, file.DurationMs ?? 0,
            burned, video?.FrameRate, segments);
        var stream = new HlsStream(job, file.SizeBytes, video?.Width, video?.Height);
        _streams.Keep(session.Id, tracks, stream);
        return ServiceResult<HlsStream>.Ok(stream);
    }

    private async Task<BurnedSubtitle> BurnAsync(MediaStream subtitle, IReadOnlyList<MediaStream> streams, string videoPath,
        CancellationToken cancellationToken)
    {
        if (!subtitle.IsTextBased)
        {
            return subtitle.IsExternal ? new BurnedSubtitle(true, null, subtitle.ExternalPath) : new BurnedSubtitle(true, subtitle.IndexHint, null);
        }

        // The filter reads a file only as UTF-8 and drops the lines it cannot time, so it gets a repaired copy
        if (subtitle is { IsExternal: true, ExternalPath: { } path })
        {
            var external = new SubtitleSource(SubtitleKey(subtitle), path, null, subtitle.Codec, subtitle.Language);
            return new BurnedSubtitle(false, null, await _subtitles.BurnableAsync(external, [], cancellationToken));
        }

        var sameVideo = streams
            .Where(s => s is { Kind: StreamKind.Subtitle, IsTextBased: true, IsExternal: false, IndexHint: not null })
            .Select(s => new SubtitleSource(SubtitleKey(s), videoPath, s.IndexHint, s.Codec, s.Language))
            .ToList();
        var source = sameVideo.Single(s => s.StreamIndex == subtitle.IndexHint);
        return new BurnedSubtitle(false, null, await _subtitles.BurnableAsync(source, sameVideo, cancellationToken));
    }

    // The same names as the WebVTT the file endpoint hands out, so one read of the video serves both
    private static string SubtitleKey(MediaStream stream) => $"{stream.MediaFileId:N}-{stream.Id}";
}
