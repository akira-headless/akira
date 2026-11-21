using Akira.Contracts.Accounts;
using Akira.Contracts.Playback;
using Microsoft.EntityFrameworkCore;
using Akira.Core.Playback;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Playback;
using ContractMarkerKind = Akira.Contracts.Playback.MarkerKind;

namespace Akira.Server.Bridges;

/// <summary>
/// Represents playback for bridges, through the same playback service as the API: the plan, the session and what the user's rules
/// pick are the same whichever way a client came in.
/// </summary>
public sealed class PlaybackAccess : IPlaybackAccess
{
    private readonly IPlaybackService _playback;
    private readonly IProgressiveService _progressive;
    private readonly ITrickplayService _trickplay;
    private readonly AkiraDbContext _dbContext;

    public PlaybackAccess(
        IPlaybackService playback,
        IProgressiveService progressive,
        ITrickplayService trickplay,
        AkiraDbContext dbContext)
    {
        _playback = playback;
        _progressive = progressive;
        _trickplay = trickplay;
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlaybackMarker>> MarkersAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await _dbContext.MediaFiles
            .AsNoTracking()
            .Include(f => f.Chapters)
            .SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null)
        {
            return [];
        }

        // The same as the plan's markers
        var scan = await _dbContext.MarkerScans.AsNoTracking().SingleOrDefaultAsync(s => s.MediaFileId == fileId, cancellationToken);
        var chapterMarkers = Markers.FromChapters(file.Chapters, file.DurationMs);
        return Markers.WithScan(chapterMarkers, scan)
            .Select(marker => new PlaybackMarker(Marker(marker.Kind), marker.StartMs, marker.EndMs))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PlaybackTrickplay?> TrickplayAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var sheets = await _trickplay.GetAsync(fileId, cancellationToken);
        if (sheets is null)
        {
            return null;
        }

        return ToTrickplay(sheets);
    }

    /// <inheritdoc />
    public async Task<PlannedPlayback?> PlanAsync(Caller caller, PlaybackRequest request, CancellationToken cancellationToken)
    {
        var capabilities = request.Capabilities;
        IReadOnlyList<string> accessModes = capabilities.Hls ? ["http", "hls"] : ["http"];
        var model = new CapabilitiesModel(capabilities.VideoCodecs, capabilities.Hdr, capabilities.AudioPassthrough, capabilities.AudioCodecs,
            capabilities.SubtitleFormats, accessModes, capabilities.MaxHeight, capabilities.Containers);
        var planRequest = new PlaybackPlanRequest(model, request.FileId);

        // Not started: a bridge's client asks for a plan to show an item, and often never plays it
        var result = await _playback.CreatePlanAsync(BridgePrincipal.For(caller), request.ItemId, planRequest, cancellationToken);
        if (!result.Succeeded)
        {
            return null;
        }

        var plan = result.Value!;
        TrickplayModel? sheets = null;
        if (plan.TrickplayUrl is not null)
        {
            sheets = await _trickplay.GetAsync(plan.Version.FileId, cancellationToken);
        }

        var markers = plan.Markers.Select(marker => new PlaybackMarker(Marker(marker.Kind), marker.StartMs, marker.EndMs)).ToList();
        var trickplay = sheets is null ? null : ToTrickplay(sheets);
        return new PlannedPlayback(plan.SessionId, plan.Item.Id, plan.Version.FileId, plan.ResumePositionMs, plan.Version.DurationMs,
            plan.Audio.StreamId, plan.Subtitle.StreamId, plan.Transcode is not null, plan.Transcode?.BurnedSubtitleStreamId,
            plan.Transcode?.Reasons ?? [], markers, trickplay);
    }

    /// <inheritdoc />
    public async Task<bool> StartAsync(Caller caller, Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _playback.StartAsync(BridgePrincipal.For(caller), sessionId, cancellationToken);
        return result.Succeeded;
    }

    /// <inheritdoc />
    public async Task<bool> ProgressAsync(Caller caller, Guid sessionId, long positionMs, bool paused, CancellationToken cancellationToken)
    {
        var request = new ProgressRequest(Math.Max(0, positionMs), paused);
        var result = await _playback.ReportProgressAsync(BridgePrincipal.For(caller), sessionId, request, cancellationToken);
        return result.Succeeded;
    }

    /// <inheritdoc />
    public async Task<bool> TracksAsync(Caller caller, Guid sessionId, string? audioStreamId, string? subtitleStreamId, bool subtitlesOff,
        bool remember, CancellationToken cancellationToken)
    {
        var request = new TracksRequest(audioStreamId, subtitleStreamId, subtitlesOff);
        var result = await _playback.ChangeTracksAsync(BridgePrincipal.For(caller), sessionId, request, remember, cancellationToken);
        return result.Succeeded;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProgressiveStream>> ProgressiveAsync(Caller caller, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken) =>
        _progressive.DescribeAsync(BridgePrincipal.For(caller), fileIds, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> StopAsync(Caller caller, Guid sessionId, long? positionMs, CancellationToken cancellationToken)
    {
        // A client that does not say where it stopped is taken to be where it last said it was. Jellyfin takes it for the
        // end, which marks a film watched that someone left halfway
        var position = positionMs ?? await _dbContext.PlaybackSessions
            .AsNoTracking()
            .Where(session => session.Id == sessionId && session.UserId == caller.UserId)
            .Select(session => (long?)session.PositionMs)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        var result = await _playback.StopAsync(BridgePrincipal.For(caller), sessionId, new StopRequest(Math.Max(0, position)), cancellationToken);
        return result.Succeeded;
    }

    /// <inheritdoc />
    public Task<bool> EndAsync(Caller caller, Guid sessionId, CancellationToken cancellationToken) =>
        _playback.EndAsync(BridgePrincipal.For(caller), sessionId, cancellationToken);

    /// <inheritdoc />
    public async Task<byte[]?> TrickplaySheetAsync(Guid fileId, int sheet, CancellationToken cancellationToken)
    {
        var path = await _trickplay.GetSheetPathAsync(fileId, sheet, cancellationToken);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private static PlaybackTrickplay ToTrickplay(TrickplayModel sheets) =>
        new(sheets.IntervalMs, sheets.Width, sheets.Height, sheets.Columns, sheets.Rows, sheets.Count);

    private static ContractMarkerKind Marker(Core.Playback.MarkerKind kind) => kind switch
    {
        Core.Playback.MarkerKind.Credits => ContractMarkerKind.Credits,
        Core.Playback.MarkerKind.Recap => ContractMarkerKind.Recap,
        Core.Playback.MarkerKind.Preview => ContractMarkerKind.Preview,
        _ => ContractMarkerKind.Intro
    };
}
