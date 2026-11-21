using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Akira.Server.Authentication;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Library;
using Akira.Core.Playback;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Interfaces.Probing;
using Akira.Infrastructure.Probing;
using Akira.Infrastructure.Transcoding;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using Akira.Server.Models.Files;
using Akira.Server.Models.Playback;
using Akira.Server.Playback;

namespace Akira.Server.Services.Playback;

public class PlaybackService : IPlaybackService
{
    // Sessions nobody reported on for this long are left over from a client that went away
    private static readonly TimeSpan StaleSession = TimeSpan.FromDays(2);

    // A stream request starts the device's plan for that file when the plan is at most this old
    private static readonly TimeSpan PlannedSession = TimeSpan.FromHours(12);
    private const int MinHeight = 144;

    private readonly AkiraDbContext _dbContext;
    private readonly ITrackPreferenceService _preferences;
    private readonly IOptions<PlaybackOptions> _playbackOptions;
    private readonly IOptions<ArrOptions> _arrOptions;
    private readonly IEventPublisher _events;
    private readonly IHlsTranscoder _transcoder;
    private readonly IOptions<TranscodingOptions> _transcodingOptions;
    private readonly IProbeService _probe;
    private readonly FfprobeStatus _ffprobeStatus;
    private readonly ILibraryEventService _libraryEvents;
    private readonly ProgressEvents _progressEvents;
    private readonly TimeProvider _time;
    private readonly ILogger<PlaybackService> _logger;

    public PlaybackService(
        AkiraDbContext dbContext,
        ITrackPreferenceService preferences,
        IOptions<PlaybackOptions> playbackOptions,
        IOptions<ArrOptions> arrOptions,
        IEventPublisher events,
        IHlsTranscoder transcoder,
        IOptions<TranscodingOptions> transcodingOptions,
        IProbeService probe,
        FfprobeStatus ffprobeStatus,
        ILibraryEventService libraryEvents,
        ProgressEvents progressEvents,
        TimeProvider time,
        ILogger<PlaybackService> logger)
    {
        _dbContext = dbContext;
        _preferences = preferences;
        _playbackOptions = playbackOptions;
        _arrOptions = arrOptions;
        _events = events;
        _transcoder = transcoder;
        _transcodingOptions = transcodingOptions;
        _probe = probe;
        _ffprobeStatus = ffprobeStatus;
        _libraryEvents = libraryEvents;
        _progressEvents = progressEvents;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> StartAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(user, sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, $"Session {sessionId} does not exist or has ended.");
        }

        await MarkStartedAsync(session, cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }

    /// <inheritdoc />
    public async Task StartFileAsync(ClaimsPrincipal user, Guid fileId, CancellationToken cancellationToken)
    {
        // The plan a device made for this file last, as the stream url carries no session
        var userId = user.GetUserId();
        var deviceId = user.GetDeviceId();
        var since = _time.GetUtcNow() - PlannedSession;
        var planned = await _dbContext.PlaybackSessions
            .Where(s => s.UserId == userId && s.DeviceId == deviceId && s.MediaFileId == fileId && s.EndedAt == null && s.StartedAt == null
                        && s.LastProgressAt > since)
            .ToListAsync(cancellationToken);
        if (planned.MaxBy(s => s.LastProgressAt) is { } session)
        {
            await MarkStartedAsync(session, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PlaybackPlan>> CreatePlanAsync(
        ClaimsPrincipal user,
        Guid itemId,
        PlaybackPlanRequest request,
        CancellationToken cancellationToken)
    {
        await ProbeUnprobedAsync(itemId, cancellationToken);

        var item = await _dbContext.MediaItems.AsNoTracking()
            .Include(item => item.Files).ThenInclude(file => file.Streams)
            .Include(item => item.Files).ThenInclude(file => file.Chapters)
            .Include(item => item.Parent!).ThenInclude(season => season.Parent)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == itemId, cancellationToken);

        if (item is null)
        {
            return ServiceResult<PlaybackPlan>.Fail(ServiceError.NotFound, $"Item {itemId} does not exist.");
        }

        if (item.Type is not (MediaItemType.Movie or MediaItemType.Episode))
        {
            return ServiceResult<PlaybackPlan>.Fail(ServiceError.Invalid, "Only movies and episodes can be played. Pick an episode of this series.");
        }

        if (item.Files.Count == 0)
        {
            return ServiceResult<PlaybackPlan>.Fail(ServiceError.Conflict, "This item has no file yet.");
        }

        if (request.FileId is { } fileId && item.Files.All(file => file.Id != fileId))
        {
            return ServiceResult<PlaybackPlan>.Fail(ServiceError.Invalid, $"File {fileId} is not a version of this item.");
        }

        var capabilities = ToCapabilities(request.Capabilities);
        var order = _arrOptions.Value.Instances
            .Select((instance, index) => (instance.Key, index))
            .ToDictionary(e => e.Key, e => e.index);
        var versions = item.Files
            .Select(file =>
            {
                var video = file.Streams.Where(s => s.Kind == StreamKind.Video).OrderBy(s => s.SortOrder).FirstOrDefault();
                var instanceOrder = order.GetValueOrDefault(file.InstanceKey, int.MaxValue);
                return new VersionCandidate(file, video, instanceOrder);
            })
            .ToList();
        var version = VersionSelector.Choose(versions, capabilities, request.FileId)!;
        var file = version.File;

        var series = item.Type == MediaItemType.Episode ? item.Parent?.Parent : null;
        var userId = user.GetUserId()!.Value;
        var streams = file.Streams.OrderBy(s => s.SortOrder).ToList();
        var (selection, sources, transcode) = await DecideAsync(userId, item, series, file, capabilities, cancellationToken);
        if (sources.Count == 0 && transcode is null)
        {
            return ServiceResult<PlaybackPlan>.Fail(ServiceError.Conflict,
                "No source this client can open: accept http, configure Playback:ClientPathMappings for file paths, or accept hls " +
                "with ffmpeg installed and Transcoding:MaxConcurrent above 0.");
        }

        var watch = await _dbContext.WatchStates.AsNoTracking()
            .SingleOrDefaultAsync(w => w.UserId == userId && w.IdentityKey == item.IdentityKey, cancellationToken);

        var now = _time.GetUtcNow();
        await _dbContext.PlaybackSessions
            .Where(s => s.UserId == userId && (s.EndedAt != null || s.LastProgressAt < now - StaleSession))
            .ExecuteDeleteAsync(cancellationToken);

        // A device plays one thing at a time, so what it had transcoded before is over, whether it said so or not. A
        // segment asked for after all starts that transcode again
        var deviceId = user.GetDeviceId();
        if (deviceId is not null)
        {
            var transcoding = await _dbContext.PlaybackSessions.AsNoTracking()
                .Where(s => s.UserId == userId && s.DeviceId == deviceId && s.EndedAt == null && s.StreamDecision != null)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            foreach (var previous in transcoding)
            {
                _transcoder.Stop(previous);
            }
        }

        var session = new PlaybackSession
        {
            UserId = userId,
            DeviceId = deviceId,
            MediaItemId = item.Id,
            MediaFileId = file.Id,
            IdentityKey = item.IdentityKey,
            MemoryKey = series?.IdentityKey ?? item.IdentityKey,
            MemoryScope = series is null ? PreferenceScope.Item : PreferenceScope.Series,
            PositionMs = watch?.PositionMs ?? 0,
            AudioStreamId = selection.Audio.Stream?.Id,
            SubtitleStreamId = selection.Subtitle.Stream?.Id,
            StreamDecision = transcode?.ToToken(),
            LastProgressAt = now
        };
        _dbContext.PlaybackSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The segments go first when the file itself will not play; the file stays as a fallback for a client that tries anyway
        if (transcode is not null)
        {
            sources.Insert(0, new PlanSource(SourceMode.Hls, null, StreamRoutes.HlsMasterUrl(session.Id), false));
        }

        var instanceName = _arrOptions.Value.Instances.FirstOrDefault(i => i.Key == file.InstanceKey)?.DisplayName ?? file.InstanceKey;
        var hasTrickplay = await _dbContext.TrickplaySets.AnyAsync(t => t.MediaFileId == file.Id && t.Status == TrickplayStatus.Ready, cancellationToken);
        var scan = await _dbContext.MarkerScans.AsNoTracking().SingleOrDefaultAsync(s => s.MediaFileId == file.Id, cancellationToken);
        var chapterMarkers = Markers.FromChapters(file.Chapters, file.DurationMs);
        var markers = Markers.WithScan(chapterMarkers, scan)
            .Select(marker => new PlanMarker(marker.Kind, marker.StartMs, marker.EndMs, marker.Source))
            .ToList();
        var clientPaths = _playbackOptions.Value.CreateClientPathMapper();

        var planItem = new PlanItem(item.Id, item.Type, item.Title, series?.Title, item.SeasonNumber, item.EpisodeNumber);
        var planVersion = new PlanVersion(file.Id, file.InstanceKey, instanceName, version.Reason, version.IsCompatible, file.DurationMs);
        var audioPick = Pick(selection.Audio, file.Id, clientPaths);
        var subtitlePick = Pick(selection.Subtitle, file.Id, clientPaths);
        var tracks = streams
            .Where(s => s.Kind != StreamKind.Video)
            .Select(s => new PlanTrack(s.Id, s.Kind, Match(s, file.Id, clientPaths)))
            .ToList();
        var chapters = file.Chapters
            .OrderBy(c => c.SortOrder)
            .Select(c => new ChapterSummary(c.Title, c.StartMs, c.EndMs))
            .ToList();
        var planTranscode = transcode is null ? null : ToModel(transcode, selection.Subtitle.Stream);
        var trickplayUrl = hasTrickplay ? StreamRoutes.TrickplayUrl(file.Id) : null;
        return ServiceResult<PlaybackPlan>.Ok(new PlaybackPlan(
            session.Id,
            planItem,
            planVersion,
            watch?.PositionMs ?? 0,
            sources,
            audioPick,
            subtitlePick,
            tracks,
            chapters,
            planTranscode,
            markers,
            trickplayUrl));
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<Guid>> PlaysAsIsAsync(
        ClaimsPrincipal user,
        IReadOnlyList<Guid> fileIds,
        CapabilitiesModel capabilities,
        CancellationToken cancellationToken)
    {
        var userId = user.GetUserId()!.Value;
        var model = ToCapabilities(capabilities);
        var files = await _dbContext.MediaFiles.AsNoTracking()
            .Where(file => fileIds.Contains(file.Id))
            .Include(file => file.Streams)
            .Include(file => file.MediaItem).ThenInclude(item => item.Parent!).ThenInclude(season => season.Parent)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var asIs = new HashSet<Guid>();
        foreach (var file in files)
        {
            if (!CanTranscode(file, model))
            {
                asIs.Add(file.Id);
                continue;
            }

            // Most files are transcoded for their container or video whatever tracks are picked; only the others need the
            // user's track rules, which take a few reads each
            var video = file.Streams.Where(s => s.Kind == StreamKind.Video).OrderBy(s => s.SortOrder).FirstOrDefault();
            if (!StreamDecision.Decide(file, video, null, model, Sources(file, model).Count == 0).DirectPlay)
            {
                continue;
            }

            var item = file.MediaItem;
            var series = item.Type == MediaItemType.Episode ? item.Parent?.Parent : null;
            var (_, _, transcode) = await DecideAsync(userId, item, series, file, model, cancellationToken);
            if (transcode is null)
            {
                asIs.Add(file.Id);
            }
        }

        return asIs;
    }

    // The tracks the user's rules pick in a file, the sources the client can open, and what a transcode changes, null when
    // the file goes out as it is
    private async Task<(TrackSelection Selection, List<PlanSource> Sources, StreamDecision? Transcode)> DecideAsync(
        Guid userId,
        MediaItem item,
        MediaItem? series,
        MediaFile file,
        DeviceCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        var rules = await _preferences.GetEffectiveAsync(userId, item.IdentityKey, series?.IdentityKey, cancellationToken);
        var streams = file.Streams.OrderBy(s => s.SortOrder).ToList();

        // Audio the client cannot decode is no reason to fall back to a commentary track when the server can encode it, and a
        // subtitle it cannot show none to skip the forced one, which can be burned in
        var resolveFor = CanTranscode(file, capabilities)
            ? capabilities with { AudioPassthrough = null, AudioDecode = null, SubtitleFormats = null }
            : capabilities;
        var selection = TrackResolver.Resolve(streams, item.OriginalLanguage ?? series?.OriginalLanguage, rules, resolveFor);
        var sources = Sources(file, capabilities);
        var video = streams.FirstOrDefault(s => s.Kind == StreamKind.Video);
        var transcode = HlsDecision(file, video, selection.Audio.Stream, capabilities, sources.Count == 0, selection.Subtitle.Stream);
        return (selection, sources, transcode);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> ReportProgressAsync(
        ClaimsPrincipal user,
        Guid sessionId,
        ProgressRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PositionMs < 0)
        {
            return ServiceResult<bool>.Fail(ServiceError.Invalid, "positionMs cannot be negative.");
        }

        var session = await FindSessionAsync(user, sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, $"Session {sessionId} does not exist or has ended.");
        }

        // A client that reports where it is plays, also when it never said that it started
        await MarkStartedAsync(session, cancellationToken);

        var now = _time.GetUtcNow();
        var pausedOrResumed = session.IsPaused != request.IsPaused;

        // A report that was on its way when the session stopped must not put back the resume position the stop just wrote.
        // The session row is updated only while it is open, and that update holds it until the watch state is saved too,
        // so a stop either comes first and this report does nothing, or waits and has the last word.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updated = await _dbContext.PlaybackSessions
            .Where(s => s.Id == session.Id && s.EndedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.PositionMs, request.PositionMs)
                .SetProperty(s => s.IsPaused, request.IsPaused)
                .SetProperty(s => s.LastProgressAt, now)
                .SetProperty(s => s.UpdatedAt, now), cancellationToken);
        if (updated == 0)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, $"Session {sessionId} does not exist or has ended.");
        }

        var watch = await GetOrAddWatchStateAsync(session.UserId, session.IdentityKey, cancellationToken);
        watch.PositionMs = request.PositionMs;
        watch.LastPlayedAt = now;
        await SaveWatchStateAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The reports while playing are not stored events, other screens only need the position once it rests. A remote
        // that shows the playing position gets it live
        if (pausedOrResumed)
        {
            await PublishWatchStateAsync(session, watch, WatchStateReasons.Progress);
        }

        _progressEvents.Publish(session.UserId, new PlaybackPayload(session.Id, session.MediaItemId, session.IdentityKey, session.MediaFileId,
            session.DeviceId, request.PositionMs, session.StreamDecision is not null, null));
        return ServiceResult<bool>.Ok(true);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> ChangeTracksAsync(
        ClaimsPrincipal user,
        Guid sessionId,
        TracksRequest request,
        bool remember,
        CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(user, sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, $"Session {sessionId} does not exist or has ended.");
        }

        if (request.SubtitlesOff && request.SubtitleStreamId is not null)
        {
            return ServiceResult<bool>.Fail(ServiceError.Invalid, "Send subtitleStreamId or subtitlesOff, not both.");
        }

        var ids = new[] { request.AudioStreamId, request.SubtitleStreamId, session.AudioStreamId }.OfType<string>().ToList();
        var streams = await _dbContext.MediaStreams.AsNoTracking()
            .Where(s => s.MediaFileId == session.MediaFileId && ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var audio = request.AudioStreamId is null ? null : streams.GetValueOrDefault(request.AudioStreamId);
        var subtitle = request.SubtitleStreamId is null ? null : streams.GetValueOrDefault(request.SubtitleStreamId);
        if ((request.AudioStreamId is not null && audio?.Kind != StreamKind.Audio)
            || (request.SubtitleStreamId is not null && subtitle?.Kind != StreamKind.Subtitle))
        {
            return ServiceResult<bool>.Fail(ServiceError.Invalid, "A stream id is not an audio or subtitle track of this session's file.");
        }

        // Remembered as what the track is, not its id, so the next episode or an upgraded file finds its own copy of it. A
        // change the user did not choose (a bridge turning subtitles off for one playback) only changes this session
        TrackPreference? memory = null;
        if (remember)
        {
            memory = await _dbContext.TrackPreferences.SingleOrDefaultAsync(p =>
                p.UserId == session.UserId && p.Scope == session.MemoryScope && p.ScopeKey == session.MemoryKey, cancellationToken);
            if (memory is null)
            {
                memory = new TrackPreference { UserId = session.UserId, Scope = session.MemoryScope, ScopeKey = session.MemoryKey };
                _dbContext.TrackPreferences.Add(memory);
            }
        }

        if (audio is not null)
        {
            memory?.RememberedAudio = TrackSignatures.For(audio);

            // An HLS stream follows the session: its next segment request starts ffmpeg again with this track
            if (StreamDecision.FromToken(session.StreamDecision) is { } decision)
            {
                var previous = session.AudioStreamId is null ? null : streams.GetValueOrDefault(session.AudioStreamId);
                session.StreamDecision = decision.WithAudio(previous, audio).ToToken();
            }

            session.AudioStreamId = audio.Id;
        }

        // A subtitle the client cannot show is burned into an HLS stream from its next segment on
        if (StreamDecision.FromToken(session.StreamDecision) is { } current && (subtitle is not null || request.SubtitlesOff))
        {
            session.StreamDecision = current.WithSubtitle(subtitle).ToToken();
        }

        if (subtitle is not null)
        {
            memory?.RememberedSubtitle = TrackSignatures.For(subtitle);
            memory?.RememberedSubtitleOff = null;
            session.SubtitleStreamId = subtitle.Id;
        }
        else if (request.SubtitlesOff)
        {
            memory?.RememberedSubtitle = null;
            memory?.RememberedSubtitleOff = true;
            session.SubtitleStreamId = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (memory is not null)
        {
            await _events.PublishAsync([new EventDraft(EventTypes.TrackPreferenceChanged,
                new TrackPreferencePayload(session.MemoryScope, session.MemoryKey), session.UserId)]);
        }

        return ServiceResult<bool>.Ok(true);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserState>> StopAsync(
        ClaimsPrincipal user,
        Guid sessionId,
        StopRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PositionMs < 0)
        {
            return ServiceResult<UserState>.Fail(ServiceError.Invalid, "positionMs cannot be negative.");
        }

        var session = await FindSessionAsync(user, sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<UserState>.Fail(ServiceError.NotFound, $"Session {sessionId} does not exist or has ended.");
        }

        // A client that plays reports progress or fetches the stream first, so a session stopped before it started is a plan
        // nobody played (a screen left while it loaded): it ends without events and the watch state stays
        if (session.StartedAt is null && await EndUnstartedAsync(session, cancellationToken))
        {
            var current = await _dbContext.WatchStates.AsNoTracking()
                .SingleOrDefaultAsync(w => w.UserId == session.UserId && w.IdentityKey == session.IdentityKey, cancellationToken);
            return ServiceResult<UserState>.Ok(new UserState(current?.PositionMs ?? 0, current?.IsPlayed ?? false, current?.PlayCount ?? 0,
                current?.LastPlayedAt));
        }

        if (session.EndedAt is not null)
        {
            return ServiceResult<UserState>.Fail(ServiceError.NotFound, $"Session {sessionId} does not exist or has ended.");
        }

        var duration = await _dbContext.MediaFiles
            .Where(f => f.Id == session.MediaFileId)
            .Select(f => f.DurationMs)
            .SingleOrDefaultAsync(cancellationToken);
        if (duration is null)
        {
            duration = await _dbContext.MediaItems
                .Where(i => i.Id == session.MediaItemId)
                .Select(i => (long?)i.RuntimeMinutes * 60_000)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var settings = _playbackOptions.Value;
        var now = _time.GetUtcNow();

        session.PositionMs = request.PositionMs;
        session.EndedAt = now;

        var watch = await GetOrAddWatchStateAsync(session.UserId, session.IdentityKey, cancellationToken);
        var outcome = WatchProgress.Stop(request.PositionMs, duration, settings.PlayedThreshold, settings.MinimumResume, watch.PositionMs);
        watch.PositionMs = outcome.ResumePositionMs;
        watch.LastPlayedAt = now;
        if (outcome.Played)
        {
            watch.IsPlayed = true;
            watch.PlayCount++;
        }

        await SaveWatchStateAsync(cancellationToken);

        // After the session is saved as ended: a segment asked for in between would start ffmpeg again
        _transcoder.Stop(session.Id);
        _progressEvents.Forget(session.Id);
        await PublishWatchStateAsync(session, watch, WatchStateReasons.Stop);
        var payload = new PlaybackPayload(session.Id, session.MediaItemId, session.IdentityKey, session.MediaFileId, session.DeviceId,
            request.PositionMs, session.StreamDecision is not null, outcome.Played);
        await _events.PublishAsync([new EventDraft(EventTypes.PlaybackStopped, payload, session.UserId)]);
        return ServiceResult<UserState>.Ok(new UserState(watch.PositionMs, watch.IsPlayed, watch.PlayCount, watch.LastPlayedAt));
    }

    /// <inheritdoc />
    public async Task<bool> EndAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await FindSessionAsync(user, sessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        // Nobody heard a session start that was only planned, so nobody hears it end
        if (session.StartedAt is null && await EndUnstartedAsync(session, cancellationToken))
        {
            return true;
        }

        if (session.EndedAt is not null)
        {
            return false;
        }

        session.EndedAt = _time.GetUtcNow();
        await _dbContext.SaveChangesAsync(cancellationToken);
        _transcoder.Stop(session.Id);
        _progressEvents.Forget(session.Id);

        // Stopped where the user already was, so a reader of the events (Trakt) sees the playback end without a new position
        var storedPosition = await _dbContext.WatchStates
            .Where(w => w.UserId == session.UserId && w.IdentityKey == session.IdentityKey)
            .Select(w => (long?)w.PositionMs)
            .SingleOrDefaultAsync(cancellationToken);
        var position = storedPosition ?? 0;
        var payload = new PlaybackPayload(session.Id, session.MediaItemId, session.IdentityKey, session.MediaFileId, session.DeviceId, position,
            session.StreamDecision is not null, false);
        await _events.PublishAsync([new EventDraft(EventTypes.PlaybackStopped, payload, session.UserId)]);
        return true;
    }

    // Claimed in the database like the start, so a session that started in between ends as one that played
    private async Task<bool> EndUnstartedAsync(PlaybackSession session, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var ended = await _dbContext.PlaybackSessions
            .Where(s => s.Id == session.Id && s.StartedAt == null && s.EndedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.EndedAt, now)
                .SetProperty(s => s.UpdatedAt, now), cancellationToken);
        if (ended == 0)
        {
            // It started or ended in between; the caller goes on with what it is now
            await _dbContext.Entry(session).ReloadAsync(cancellationToken);
            return false;
        }

        _transcoder.Stop(session.Id);
        return true;
    }

    // Claimed in the database, so two reports at once say it once
    private async Task MarkStartedAsync(PlaybackSession session, CancellationToken cancellationToken)
    {
        if (session.StartedAt is not null)
        {
            return;
        }

        var now = _time.GetUtcNow();
        var claimed = await _dbContext.PlaybackSessions
            .Where(s => s.Id == session.Id && s.StartedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.StartedAt, now)
                .SetProperty(s => s.LastProgressAt, now)
                .SetProperty(s => s.UpdatedAt, now), cancellationToken);
        session.StartedAt = now;
        _dbContext.Entry(session).Property(s => s.StartedAt).IsModified = false;
        if (claimed > 0)
        {
            await PublishStartedAsync(session);
        }
    }

    private Task PublishStartedAsync(PlaybackSession session)
    {
        var payload = new PlaybackPayload(session.Id, session.MediaItemId, session.IdentityKey, session.MediaFileId, session.DeviceId,
            session.PositionMs, session.StreamDecision is not null, null);
        return _events.PublishAsync([new EventDraft(EventTypes.PlaybackStarted, payload, session.UserId)]);
    }

    private Task PublishWatchStateAsync(PlaybackSession session, WatchState watch, string reason)
    {
        var payload = new WatchStatePayload(session.MediaItemId, session.IdentityKey, watch.PositionMs, watch.IsPlayed, watch.PlayCount,
            watch.LastPlayedAt, reason);
        return _events.PublishAsync([new EventDraft(EventTypes.WatchStateChanged, payload, session.UserId)]);
    }

    private Task<PlaybackSession?> FindSessionAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken)
    {
        // Another user's session is answered the same as one that does not exist
        var userId = user.GetUserId();
        return _dbContext.PlaybackSessions
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId && s.EndedAt == null, cancellationToken);
    }

    private async Task<WatchState> GetOrAddWatchStateAsync(Guid userId, string identityKey, CancellationToken cancellationToken)
    {
        var watch = await _dbContext.WatchStates.SingleOrDefaultAsync(w => w.UserId == userId && w.IdentityKey == identityKey, cancellationToken);
        if (watch is null)
        {
            watch = new WatchState { UserId = userId, IdentityKey = identityKey };
            _dbContext.WatchStates.Add(watch);
        }

        return watch;
    }

    private async Task SaveWatchStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (_dbContext.ChangeTracker.Entries<WatchState>().Any(e => e.State == EntityState.Added))
        {
            // Two devices of the same user created the row at the same moment. The other one won,
            // so this update goes onto its row instead.
            var added = _dbContext.ChangeTracker.Entries<WatchState>().Single(e => e.State == EntityState.Added);
            var values = added.Entity;
            added.State = EntityState.Detached;

            var existing = await _dbContext.WatchStates.SingleAsync(w => w.UserId == values.UserId && w.IdentityKey == values.IdentityKey,
                cancellationToken);
            existing.PositionMs = values.PositionMs;
            existing.LastPlayedAt = values.LastPlayedAt;
            existing.IsPlayed |= values.IsPlayed;
            existing.PlayCount += values.PlayCount;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private List<PlanSource> Sources(MediaFile file, DeviceCapabilities capabilities)
    {
        var sources = new List<PlanSource>();

        // A path is only offered when a mapping matched: the server's own path means nothing to another machine
        if (capabilities.Accepts("filePath") && _playbackOptions.Value.CreateClientPathMapper().TryMap(file.Path, out var path))
        {
            sources.Add(new PlanSource(SourceMode.FilePath, path, null, false));
        }

        if (capabilities.Accepts("http"))
        {
            sources.Add(new PlanSource(SourceMode.Http, null, StreamRoutes.FileStreamUrl(file.Id), true));
        }

        return sources;
    }

    // Played before the probe worker reached it, a file has no tracks and no duration, so the plan would offer it as it is: a
    // browser then plays the video of an AC-3 file without its sound. Reading the tracks first takes a moment, a header only.
    private async Task ProbeUnprobedAsync(Guid itemId, CancellationToken cancellationToken)
    {
        if (_ffprobeStatus.Current.IsAvailable == false)
        {
            return;
        }

        var pending = await _dbContext.MediaFiles.AsNoTracking()
            .Where(file => file.MediaItemId == itemId && file.ProbeStatus == ProbeStatus.Pending)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        try
        {
            var result = await _probe.ProbeFilesAsync(pending, cancellationToken);
            await _libraryEvents.PublishProbeAsync(result, null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The probe worker tries again; this play goes ahead with what is known
            _logger.LogWarning(exception, "Probing {Count} files of item {ItemId} before playing it failed", pending.Count, itemId);
        }
    }

    private StreamDecision? HlsDecision(MediaFile file, MediaStream? video, MediaStream? audio, DeviceCapabilities capabilities, bool hlsOnly,
        MediaStream? subtitle)
    {
        if (!CanTranscode(file, capabilities))
        {
            return null;
        }

        var decision = StreamDecision.Decide(file, video, audio, capabilities, hlsOnly, subtitle);
        return decision.DirectPlay ? null : decision;
    }

    // Only for a client that names hls, and the playlist lists every segment up front, which needs the duration from the probe
    private bool CanTranscode(MediaFile file, DeviceCapabilities capabilities) =>
        capabilities.AccessModes is not null
        && capabilities.Accepts("hls")
        && _transcodingOptions.Value.MaxConcurrent > 0
        && file.DurationMs is not null;

    internal static PlanTranscode ToModel(StreamDecision decision, MediaStream? subtitle)
    {
        var video = decision.CopyVideo ? "copy" : "h264";
        var audio = decision.Audio.ToString().ToLowerInvariant();
        var burnedSubtitleId = decision.Burns(subtitle) ? subtitle!.Id : null;
        return new PlanTranscode(video, audio, decision.Height, decision.ToneMap, decision.Reasons, burnedSubtitleId);
    }

    private static TrackPick Pick(TrackChoice choice, Guid fileId, PathMapper clientPaths) =>
        choice.Stream is null
            ? new TrackPick(null, choice.Reason, null)
            : new TrackPick(choice.Stream.Id, choice.Reason, Match(choice.Stream, fileId, clientPaths));

    // The shape every client finds a track by, used for subtitle requests too
    internal static TrackMatch Match(MediaStream stream, Guid fileId, PathMapper clientPaths)
    {
        var flags = new List<TrackFlag>();
        if (stream.IsDefault) flags.Add(TrackFlag.Default);
        if (stream.IsForced) flags.Add(TrackFlag.Forced);
        if (stream.IsHearingImpaired) flags.Add(TrackFlag.HearingImpaired);
        if (stream.IsCommentary) flags.Add(TrackFlag.Commentary);
        if (stream.IsDescriptive) flags.Add(TrackFlag.Descriptive);

        string? externalPath = null;
        if (stream is { IsExternal: true, ExternalPath: { } serverPath } && clientPaths.TryMap(serverPath, out var mapped))
        {
            externalPath = mapped;
        }

        var subtitleUrl = stream.IsExternal ? StreamRoutes.SubtitleUrl(fileId, stream.Id) : null;
        var webVttUrl = stream is { Kind: StreamKind.Subtitle, IsTextBased: true } ? StreamRoutes.SubtitleWebVttUrl(fileId, stream.Id) : null;
        return new TrackMatch(stream.Language, stream.Codec, stream.Channels, stream.Title, flags, stream.IndexHint, subtitleUrl, externalPath,
            webVttUrl);
    }

    private static DeviceCapabilities ToCapabilities(CapabilitiesModel? model)
    {
        if (model is null)
        {
            return DeviceCapabilities.Any;
        }

        int? maxBitrateKbps = model.MaxBitrateKbps is > 0 ? model.MaxBitrateKbps : null;

        // The height ends up in ffmpeg's scale filter, and H.264 refuses an odd one
        int? maxHeight = model.MaxHeight is > 0 ? Math.Max(MinHeight, model.MaxHeight.Value) & ~1 : null;
        return new DeviceCapabilities(Clean(model.VideoCodecs), Clean(model.Hdr), Clean(model.AudioPassthrough), Clean(model.AudioDecode),
            Clean(model.SubtitleFormats), Clean(model.AccessModes), maxHeight, Clean(model.Containers), maxBitrateKbps);
    }

    // Names of formats and codecs only: they end up in the session's stored decision, which a stray ';' or '=' would break
    private static IReadOnlyList<string>? Clean(IReadOnlyList<string>? names) =>
        names?.Where(IsCleanName).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();

    private static bool IsCleanName(string name) =>
        name is { Length: > 0 and <= 32 } && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '+');
}
