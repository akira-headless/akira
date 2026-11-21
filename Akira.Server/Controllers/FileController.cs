using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Files;
using Akira.Server.Models.Playback;
using Akira.Server.Playback;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + ItemRoutes.Files)]
public sealed class FileController : ApiController
{
    private readonly IFileService _files;
    private readonly ITrickplayService _trickplay;
    private readonly IPlaybackService _playback;

    public FileController(
        IFileService files,
        ITrickplayService trickplay,
        IPlaybackService playback)
    {
        _files = files;
        _trickplay = trickplay;
        _playback = playback;
    }

    [HttpGet(ItemRoutes.Item)]
    [Tags("Library")]
    [EndpointName("GetFile")]
    [EndpointSummary("Retrieves a version of an item with its tracks and chapters.")]
    public async Task<Results<Ok<FileDetail>, ProblemHttpResult>> GetFile(Guid id, CancellationToken cancellationToken)
    {
        var file = await _files.GetAsync(id, cancellationToken);
        return file is null
            ? TypedResults.Problem(title: "File not found", detail: $"File {id} does not exist.", statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(file);
    }

    [AcceptVerbs("GET", "HEAD", Route = StreamRoutes.FileStream)]
    [Tags("Playback")]
    [EndpointName("StreamFile")]
    [EndpointSummary("Streams a version over HTTP, with range requests for seeking.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "video/x-matroska", "video/mp4", "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    public async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> StreamFile(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await _files.DeliverVideoAsync(id, cancellationToken);

        // Fetching the file is playing it: the session of the plan starts here, a HEAD only asks what it is
        if (delivery.Status == DeliveryStatus.Ready && HttpMethods.IsGet(Request.Method))
        {
            await _playback.StartFileAsync(User, id, cancellationToken);
        }

        return MediaResults.Video(delivery, id, HttpContext);
    }

    [AcceptVerbs("GET", "HEAD", Route = StreamRoutes.FileDownload)]
    [Tags("Playback")]
    [EndpointName("DownloadFile")]
    [EndpointSummary("Sends a version as a file to save, under its name on disk, with range requests to resume. Starts no playback.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "video/x-matroska", "video/mp4", "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    public async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> DownloadFile(Guid id, CancellationToken cancellationToken) =>
        MediaResults.Download(await _files.DeliverVideoAsync(id, cancellationToken), id, HttpContext);

    [HttpGet(StreamRoutes.Subtitle)]
    [Tags("Playback")]
    [EndpointName("StreamSubtitle")]
    [EndpointSummary("Serves an external subtitle file of a version.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "application/x-subrip", "text/x-ssa", "text/vtt", "application/octet-stream")]
    public async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> StreamSubtitle(Guid id, string streamId, CancellationToken cancellationToken) =>
        MediaResults.Subtitle(await _files.DeliverSubtitleAsync(id, streamId, cancellationToken), id, streamId);

    [HttpGet(StreamRoutes.Trickplay)]
    [Tags("Playback")]
    [EndpointName("GetTrickplay")]
    [EndpointSummary("Retrieves the seek thumbnails of a version: their layout and the urls of the sheets. 404 until they are made.")]
    public async Task<Results<Ok<TrickplayModel>, ProblemHttpResult>> GetTrickplay(Guid id, CancellationToken cancellationToken) =>
        await _trickplay.GetAsync(id, cancellationToken) is { } model ? TypedResults.Ok(model) : NoThumbnails(id);

    [HttpGet(StreamRoutes.TrickplayVtt)]
    [Tags("Playback")]
    [EndpointName("GetTrickplayVtt")]
    [EndpointSummary("Retrieves the seek thumbnails as a WebVTT track, each interval pointing at its tile with #xywh.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "text/vtt")]
    public async Task<Results<ContentHttpResult, ProblemHttpResult>> GetTrickplayVtt(Guid id, CancellationToken cancellationToken) =>
        await _trickplay.GetVttAsync(id, cancellationToken) is { } vtt ? TypedResults.Text(vtt, "text/vtt") : NoThumbnails(id);

    [HttpGet(StreamRoutes.TrickplaySheet)]
    [Tags("Playback")]
    [EndpointName("GetTrickplaySheet")]
    [EndpointSummary("Retrieves one sheet of seek thumbnails.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "image/jpeg")]
    public async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> GetTrickplaySheet(Guid id, int sheet, CancellationToken cancellationToken)
    {
        if (await _trickplay.GetSheetPathAsync(id, sheet, cancellationToken) is not { } path)
        {
            return NoThumbnails(id);
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        return TypedResults.PhysicalFile(path, "image/jpeg");
    }

    [HttpGet(StreamRoutes.SubtitleWebVtt)]
    [Tags("Playback")]
    [EndpointName("StreamSubtitleWebVtt")]
    [EndpointSummary("Serves a text subtitle of a version, embedded or external, converted to WebVTT for browsers.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "text/vtt")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> StreamSubtitleWebVtt(Guid id, string streamId,
        CancellationToken cancellationToken) =>
        MediaResults.WebVtt(await _files.DeliverWebVttAsync(id, streamId, cancellationToken), id, streamId);

    private static ProblemHttpResult NoThumbnails(Guid id) =>
        TypedResults.Problem(title: "No thumbnails", detail: $"File {id} has no seek thumbnails yet, or does not exist.",
            statusCode: StatusCodes.Status404NotFound);
}
