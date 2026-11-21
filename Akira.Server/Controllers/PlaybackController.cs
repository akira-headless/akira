using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Items;
using Akira.Server.Models.Playback;
using Akira.Server.Playback;
using Akira.Server.Services.Playback;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + StreamRoutes.Sessions)]
[Tags("Playback")]
public sealed class PlaybackController : ApiController
{
    private readonly IPlaybackService _service;
    private readonly IHlsService _hls;

    public PlaybackController(
        IPlaybackService service,
        IHlsService hls)
    {
        _service = service;
        _hls = hls;
    }

    [HttpPost(ItemRoutes.ApiPrefix + ItemRoutes.Items + "/{id:guid}/playback-plan")]
    [EndpointName("CreatePlaybackPlan")]
    [EndpointSummary("Chooses the version, sources and tracks for this client and opens a playback session.")]
    public async Task<Results<Ok<PlaybackPlan>, ProblemHttpResult>> CreatePlaybackPlan(Guid id, PlaybackPlanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreatePlanAsync(User, id, request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpGet]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("ListNowPlaying")]
    [EndpointSummary("Lists what is playing now, on which device, how it is delivered and how a transcode keeps up.")]
    public async Task<Ok<IReadOnlyList<NowPlaying>>> ListNowPlaying([FromServices] INowPlayingService nowPlaying,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await nowPlaying.ListAsync(cancellationToken));

    [HttpPost("{id:guid}/progress")]
    [EndpointName("ReportPlaybackProgress")]
    [EndpointSummary("Reports the position, about every 10 seconds and on pause and seek.")]
    public async Task<Results<NoContent, ProblemHttpResult>> ReportPlaybackProgress(Guid id, ProgressRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ReportProgressAsync(User, id, request, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPost("{id:guid}/tracks")]
    [EndpointName("ChangePlaybackTracks")]
    [EndpointSummary("Reports that the user changed audio or subtitles, which is remembered for the series or movie.")]
    public async Task<Results<NoContent, ProblemHttpResult>> ChangePlaybackTracks(Guid id, TracksRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ChangeTracksAsync(User, id, request, remember: true, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPost("{id:guid}/stop")]
    [EndpointName("StopPlayback")]
    [EndpointSummary("Ends the session and marks the item played past the threshold.")]
    public async Task<Results<Ok<UserState>, ProblemHttpResult>> StopPlayback(Guid id, StopRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.StopAsync(User, id, request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpGet(StreamRoutes.HlsMaster)]
    [EndpointName("GetHlsMasterPlaylist")]
    [EndpointSummary("Retrieves the master playlist of a session whose plan offered an hls source.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, MediaResults.HlsPlaylistType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<Results<ContentHttpResult, ProblemHttpResult>> GetHlsMasterPlaylist(Guid id, CancellationToken cancellationToken) =>
        MediaResults.Playlist(await _hls.GetMasterPlaylistAsync(User, id, cancellationToken), HttpContext);

    [HttpGet(StreamRoutes.HlsMedia)]
    [EndpointName("GetHlsMediaPlaylist")]
    [EndpointSummary("Retrieves the media playlist: every segment of the version, segment n starting at n times the segment length.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, MediaResults.HlsPlaylistType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<Results<ContentHttpResult, ProblemHttpResult>> GetHlsMediaPlaylist(Guid id, CancellationToken cancellationToken) =>
        MediaResults.Playlist(await _hls.GetMediaPlaylistAsync(User, id, cancellationToken), HttpContext);

    [HttpGet(StreamRoutes.HlsSegment)]
    [EndpointName("GetHlsSegment")]
    [EndpointSummary("Retrieves a segment, transcoded on demand. A segment far from the last one, a seek, starts the transcode there.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "video/mp2t")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> GetHlsSegment(Guid id, int segment, CancellationToken cancellationToken)
    {
        var result = await _hls.GetSegmentAsync(User, id, segment, cancellationToken);

        // A segment is fetched to be played, so the session of the plan starts with the first one
        if (result.Succeeded)
        {
            await _service.StartAsync(User, id, cancellationToken);
        }

        return MediaResults.Segment(result, HttpContext);
    }
}
