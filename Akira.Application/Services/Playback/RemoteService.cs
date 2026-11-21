using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using Akira.Server.Models.Playback;
using Akira.Server.Models.Remote;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Akira.Server.Services.Playback;

public sealed class RemoteService : IRemoteService
{
    // Players report every ten seconds; a session quiet for longer than this is no longer playing on that device
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(2);

    private readonly AkiraDbContext _dbContext;
    private readonly EventHub _hub;
    private readonly IOptions<JsonOptions> _jsonOptions;
    private readonly TimeProvider _time;
    private readonly ILogger<RemoteService> _logger;

    public RemoteService(
        AkiraDbContext dbContext,
        EventHub hub,
        IOptions<JsonOptions> jsonOptions,
        TimeProvider time,
        ILogger<RemoteService> logger)
    {
        _dbContext = dbContext;
        _hub = hub;
        _jsonOptions = jsonOptions;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemoteDevice>> ListAsync(Guid userId, Guid? ownDeviceId, CancellationToken cancellationToken)
    {
        var deviceIds = _hub.RemoteDevices(userId).Where(id => id != ownDeviceId).ToList();
        if (deviceIds.Count == 0)
        {
            return [];
        }

        var devices = await _dbContext.Devices.AsNoTracking()
            .Where(d => deviceIds.Contains(d.Id))
            .ToListAsync(cancellationToken);
        var now = _time.GetUtcNow();
        var since = now - Quiet;
        var sessions = await _dbContext.PlaybackSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.DeviceId != null && deviceIds.Contains(s.DeviceId.Value)
                        && s.EndedAt == null && s.StartedAt != null && s.LastProgressAt > since)
            .ToListAsync(cancellationToken);
        var latest = sessions
            .GroupBy(s => s.DeviceId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastProgressAt).First());

        var itemIds = latest.Values.Select(s => s.MediaItemId).ToList();
        var fileIds = latest.Values.Select(s => s.MediaFileId).ToList();
        var items = await _dbContext.MediaItems.AsNoTracking()
            .Include(i => i.Parent!).ThenInclude(season => season.Parent)
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        var files = await _dbContext.MediaFiles.AsNoTracking()
            .Include(f => f.Chapters)
            .Where(f => fileIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken);
        var scans = await _dbContext.MarkerScans.AsNoTracking()
            .Where(s => fileIds.Contains(s.MediaFileId))
            .ToDictionaryAsync(s => s.MediaFileId, cancellationToken);

        return devices
            .OrderBy(d => d.Name)
            .Select(device =>
            {
                RemoteNowPlaying? nowPlaying = null;
                if (latest.TryGetValue(device.Id, out var session) && items.TryGetValue(session.MediaItemId, out var item))
                {
                    nowPlaying = NowPlaying(session, item, files.GetValueOrDefault(session.MediaFileId), scans.GetValueOrDefault(session.MediaFileId), now);
                }

                return new RemoteDevice(device.Id, device.Name, device.Client, device.Platform, nowPlaying);
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<RemoteSendResult> SendAsync(Guid userId, Guid? fromDeviceId, Guid deviceId, RemoteCommandRequest command,
        CancellationToken cancellationToken)
    {
        if (command.Type == RemoteCommandType.Play && !await _dbContext.MediaItems.AnyAsync(i => i.Id == command.ItemId, cancellationToken))
        {
            return RemoteSendResult.ItemNotFound;
        }

        var options = _jsonOptions.Value.SerializerOptions;
        var payload = new RemoteCommandPayload(command.Type, command.ItemId, command.FromStart, command.PositionMs, fromDeviceId);
        var payloadJson = JsonSerializer.SerializeToElement(payload, options);
        var message = new EventMessage(0, EventTypes.RemoteCommand, 1, payloadJson, _time.GetUtcNow());
        var messageJson = JsonSerializer.SerializeToUtf8Bytes(message, options);
        if (!_hub.SendToDevice(userId, deviceId, messageJson))
        {
            return RemoteSendResult.NotConnected;
        }

        _logger.LogInformation("Remote {Command} sent to device {DeviceId}", command.Type, deviceId);
        return RemoteSendResult.Sent;
    }

    // Between reports a playing session moves on by the time since the last one
    private static RemoteNowPlaying NowPlaying(PlaybackSession session, MediaItem item, MediaFile? file, MarkerScan? scan, DateTimeOffset now)
    {
        var durationMs = file?.DurationMs;
        var series = item.Type == MediaItemType.Episode ? item.Parent?.Parent : null;
        var position = session.PositionMs;
        if (!session.IsPaused)
        {
            position += (long)(now - session.LastProgressAt).TotalMilliseconds;
        }

        if (durationMs is { } duration)
        {
            position = Math.Min(position, duration);
        }

        var planItem = new PlanItem(item.Id, item.Type, item.Title, series?.Title, item.SeasonNumber, item.EpisodeNumber);
        var markers = file is null
            ? []
            : Markers.WithScan(Markers.FromChapters(file.Chapters, durationMs), scan)
                .Select(marker => new PlanMarker(marker.Kind, marker.StartMs, marker.EndMs, marker.Source))
                .ToList();
        return new RemoteNowPlaying(session.Id, planItem, position, durationMs, session.IsPaused, markers);
    }
}
