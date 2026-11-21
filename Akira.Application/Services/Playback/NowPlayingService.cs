using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Transcoding;
using Akira.Server.Models.Playback;

namespace Akira.Server.Services.Playback;

public sealed class NowPlayingService : INowPlayingService
{
    // Clients report every ten seconds while playing; a session quiet for longer than this went away without stopping
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(2);

    private readonly AkiraDbContext _dbContext;
    private readonly IHlsTranscoder _transcoder;
    private readonly IOptions<ArrOptions> _arrOptions;
    private readonly TimeProvider _time;

    public NowPlayingService(
        AkiraDbContext dbContext,
        IHlsTranscoder transcoder,
        IOptions<ArrOptions> arrOptions,
        TimeProvider time)
    {
        _dbContext = dbContext;
        _transcoder = transcoder;
        _arrOptions = arrOptions;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NowPlaying>> ListAsync(CancellationToken cancellationToken)
    {
        var since = _time.GetUtcNow() - Quiet;
        var sessions = await _dbContext.PlaybackSessions.AsNoTracking()
            .Where(s => s.EndedAt == null && s.StartedAt != null && s.LastProgressAt > since.AddDays(-2))
            .ToListAsync(cancellationToken);

        // A paused or seeking player may not report, a running transcode still counts as playing
        sessions = sessions.Where(s => s.LastProgressAt > since || _transcoder.Status(s.Id) is { Running: true }).ToList();
        if (sessions.Count == 0)
        {
            return [];
        }

        var userIds = sessions.Select(s => s.UserId).Distinct().ToList();
        var deviceIds = sessions.Where(s => s.DeviceId != null).Select(s => s.DeviceId!.Value).Distinct().ToList();
        var itemIds = sessions.Select(s => s.MediaItemId).Distinct().ToList();
        var fileIds = sessions.Select(s => s.MediaFileId).Distinct().ToList();

        var users = await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);
        var devices = await _dbContext.Devices.AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);
        var items = await _dbContext.MediaItems.AsNoTracking()
            .Include(i => i.Parent!).ThenInclude(season => season.Parent)
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        var files = await _dbContext.MediaFiles.AsNoTracking()
            .Include(f => f.Streams)
            .Where(f => fileIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken);
        var instances = _arrOptions.Value.Instances.ToDictionary(i => i.Key, i => i.DisplayName);

        return sessions
            .Where(s => users.ContainsKey(s.UserId) && items.ContainsKey(s.MediaItemId) && files.ContainsKey(s.MediaFileId))
            .OrderByDescending(s => s.CreatedAt)
            .Select(s =>
            {
                var device = s.DeviceId is { } id ? devices.GetValueOrDefault(id) : null;
                return Describe(s, users[s.UserId], device, items[s.MediaItemId], files[s.MediaFileId], instances);
            })
            .ToList();
    }

    private NowPlaying Describe(PlaybackSession session, User user, Device? device, MediaItem item, MediaFile file, IReadOnlyDictionary<string, string> instances)
    {
        var series = item.Type == MediaItemType.Episode ? item.Parent?.Parent : null;
        var video = file.Streams.Where(s => s.Kind == StreamKind.Video).OrderBy(s => s.SortOrder).FirstOrDefault();
        var audio = file.Streams.FirstOrDefault(s => s.Id == session.AudioStreamId);
        var subtitle = file.Streams.FirstOrDefault(s => s.Id == session.SubtitleStreamId);
        var decision = StreamDecision.FromToken(session.StreamDecision);
        var status = _transcoder.Status(session.Id);

        var nowPlayingUser = new NowPlayingUser(user.Id, user.Name);
        var nowPlayingDevice = device is null ? null : new NowPlayingDevice(device.Id, device.Name, device.Client, device.Platform);
        var planItem = new PlanItem(item.Id, item.Type, item.Title, series?.Title, item.SeasonNumber, item.EpisodeNumber);
        // The plan offers both; only a transcode that runs says the client took the segments
        var delivery = status is null ? PlaybackDelivery.Direct : PlaybackDelivery.Hls;
        var transcode = status is not null && decision is not null ? PlaybackService.ToModel(decision, subtitle) : null;
        var instanceName = instances.GetValueOrDefault(file.InstanceKey) ?? file.InstanceKey;
        var version = new NowPlayingVersion(file.Id, instanceName, file.Container, video?.Codec, video?.Width, video?.Height,
            Formats.Hdr(video?.HdrFormat));
        var stats = status is null ? null : Stats(status);

        return new NowPlaying(
            session.Id,
            nowPlayingUser,
            nowPlayingDevice,
            planItem,
            session.PositionMs,
            file.DurationMs,
            session.IsPaused,
            session.CreatedAt,
            session.LastProgressAt,
            delivery,
            transcode,
            version,
            Track(audio),
            Track(subtitle),
            stats);
    }

    private static NowPlayingTrack? Track(MediaStream? stream) => stream is null
        ? null
        : new NowPlayingTrack(stream.Id, stream.Language, stream.Codec, stream.Channels, stream.Title, stream.IsForced, stream.IsExternal);

    private static TranscoderStats Stats(TranscodeStatus status)
    {
        var last = status.History.LastOrDefault();
        var samples = status.History.Select(sample => new TranscoderSample(sample.At, sample.Speed, sample.Fps)).ToList();
        return new TranscoderStats(status.Encoder, status.Running, last?.Speed, last?.Fps, status.EncodedToMs, samples);

    }
}
