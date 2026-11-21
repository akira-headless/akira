using System.Globalization;
using System.Security.Claims;
using Akira.Contracts.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Bridges;
using Akira.Core.Enum;
using Akira.Server.Endpoints;
using Akira.Infrastructure.Transcoding;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Files;
using Akira.Server.Playback;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + SignedUrls.Route)]
[Tags("Links")]
[AllowAnonymous]
public sealed class SignedController : ApiController
{
    private readonly ISignedUrlResolver _resolver;
    private readonly IFileService _files;
    private readonly IImageDeliveryService _images;
    private readonly IHlsService _hls;
    private readonly IPlaybackService _playback;
    private readonly IProgressiveService _progressive;

    private const string DlnaFeaturesRequest = "getcontentFeatures.dlna.org";
    private const string DlnaFeatures = "contentFeatures.dlna.org";
    private const string DlnaTransferMode = "transferMode.dlna.org";

    // The file as it is, with byte ranges, and a stream made as it goes, with a time seek. A bridge's listing says the same
    private const string FileFeatures = "DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000";
    private const string TranscodeFeatures = "DLNA.ORG_OP=10;DLNA.ORG_CI=1;DLNA.ORG_FLAGS=01100000000000000000000000000000";

    public SignedController(
        ISignedUrlResolver resolver,
        IFileService files,
        IImageDeliveryService images,
        IHlsService hls,
        IPlaybackService playback,
        IProgressiveService progressive)
    {
        _resolver = resolver;
        _files = files;
        _images = images;
        _hls = hls;
        _playback = playback;
        _progressive = progressive;
    }

    [AcceptVerbs("GET", "HEAD", Route = "{token}/{name}")]
    [EndpointName("GetSigned")]
    [EndpointSummary("Serves what a signed url from a bridge points at: a file with range requests, a WebVTT subtitle or artwork.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IResult> GetSigned(string token, string name, CancellationToken cancellationToken)
    {
        var target = await _resolver.ResolveAsync(token, cancellationToken);
        if (target is null || (target.LocalOnly && !ClientAddresses.IsLocal(HttpContext)))
        {
            return TypedResults.Problem(title: "Link not valid", detail: "The url expired, or the link or device it was made for was revoked.",
                statusCode: StatusCodes.Status404NotFound);
        }

        // A web player fetches subtitles and artwork from another origin. The url itself is the credential
        Response.Headers.AccessControlAllowOrigin = "*";
        if (target.Kind == SignedKind.Hls)
        {
            return await SignedHls(target, name, cancellationToken);
        }

        if (target.Kind == SignedKind.LibraryArtwork)
        {
            return await SignedLibraryArtwork(name, cancellationToken);
        }

        if (target.Kind == SignedKind.Progressive)
        {
            return await SignedProgressive(target, cancellationToken);
        }

        // A DLNA renderer asks how a file may be played: byte ranges, as it is
        if (target.Kind == SignedKind.File && Request.Headers.ContainsKey(DlnaFeaturesRequest))
        {
            Response.Headers[DlnaFeatures] = FileFeatures;
            Response.Headers[DlnaTransferMode] = "Streaming";
        }

        if (target.Kind == SignedKind.File)
        {
            return MediaResults.Video(await _files.DeliverVideoAsync(target.Id, cancellationToken), target.Id, HttpContext);
        }

        if (target.Kind == SignedKind.Subtitle)
        {
            return MediaResults.WebVtt(await _files.DeliverWebVttAsync(target.Id, target.Detail!, cancellationToken), target.Id, target.Detail!);
        }

        // The kind, and the size after it
        var detail = target.Detail!.Split(':');
        if (!Enum.TryParse<ArtworkKind>(detail[0], out var artworkKind))
        {
            return ArtworkNotFound();
        }

        var artworkSize = detail.Length > 1 && Enum.TryParse<ArtworkSize>(detail[1], out var size) ? size : ArtworkSize.Original;
        return await Image(target.Id, LibraryAccess.ToImageKind(artworkKind), LibraryAccess.ToImageSize(artworkSize), cancellationToken);
    }

    private async Task<IResult> SignedLibraryArtwork(string name, CancellationToken cancellationToken)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || !Guid.TryParse(name[..dot], out var itemId))
        {
            return ArtworkNotFound();
        }

        if (!Enum.TryParse<ImageKind>(name[(dot + 1)..], ignoreCase: true, out var imageKind) || !Enum.IsDefined(imageKind))
        {
            return ArtworkNotFound();
        }

        return await Image(itemId, imageKind, ImageSize.Original, cancellationToken);
    }

    private async Task<IResult> Image(Guid itemId, ImageKind kind, ImageSize size, CancellationToken cancellationToken)
    {
        var delivery = await _images.DeliverAsync(itemId, kind, size, MediaResults.HeldTags(Request), cancellationToken);
        return MediaResults.Image(delivery, itemId, kind, HttpContext);
    }

    private static ProblemHttpResult ArtworkNotFound()
    {
        return TypedResults.Problem(title: "Not found", detail: "No such artwork.", statusCode: StatusCodes.Status404NotFound);
    }

    // The playlists name their parts relatively, so main.m3u8 and the segments land here under the same token
    private async Task<IResult> SignedHls(SignedTarget target, string name, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(target.Detail!).ToString();
        var identity = new ClaimsIdentity([new Claim(AkiraClaims.UserId, userId)], AkiraClaims.Scheme);
        var user = new ClaimsPrincipal(identity);
        if (name == "master.m3u8")
        {
            return MediaResults.Playlist(await _hls.GetMasterPlaylistAsync(user, target.Id, cancellationToken), HttpContext);
        }

        if (name == StreamRoutes.HlsMediaName)
        {
            return MediaResults.Playlist(await _hls.GetMediaPlaylistAsync(user, target.Id, cancellationToken), HttpContext);
        }

        if (!name.EndsWith(".ts", StringComparison.Ordinal))
        {
            return TypedResults.NotFound();
        }

        if (!int.TryParse(name[..^3], NumberStyles.None, CultureInfo.InvariantCulture, out var segment))
        {
            return TypedResults.NotFound();
        }

        var result = await _hls.GetSegmentAsync(user, target.Id, segment, cancellationToken);
        if (result.Succeeded)
        {
            await _playback.StartAsync(user, target.Id, cancellationToken);
        }

        return MediaResults.Segment(result, HttpContext);
    }

    // The file itself with byte ranges when every renderer plays it, otherwise one MPEG-TS stream that ffmpeg writes as it
    // goes, which has no byte ranges but a time seek, a new request with a new start. HEAD says the same as GET
    private async Task<IResult> SignedProgressive(SignedTarget target, CancellationToken cancellationToken)
    {
        var response = Response;
        response.Headers[DlnaTransferMode] = "Streaming";
        if (HttpMethods.IsHead(Request.Method))
        {
            if (await _progressive.DescribeAsync(target, cancellationToken) is not { } stream)
            {
                return NotPlayable();
            }

            response.Headers[DlnaFeatures] = stream.AsIs ? FileFeatures : TranscodeFeatures;
            if (stream.AsIs)
            {
                return MediaResults.Video(await _files.DeliverVideoAsync(target.Id, cancellationToken), target.Id, HttpContext);
            }

            response.ContentType = stream.ContentType;
            return TypedResults.Ok();
        }

        if (await _progressive.PlanAsync(target, cancellationToken) is not { } plan)
        {
            return NotPlayable();
        }

        if (plan.Direct is not null)
        {
            response.Headers[DlnaFeatures] = FileFeatures;
            var delivery = await _files.DeliverVideoAsync(plan.FileId, cancellationToken);
            var file = MediaResults.Video(delivery, plan.FileId, HttpContext);
            if (delivery.Status != DeliveryStatus.Ready)
            {
                await _progressive.EndAsync(plan, CancellationToken.None);
                return file;
            }

            // Counted on the way out: how far into the file the bytes a renderer took reach is where it got to
            var counted = new CountingStream(response.Body);
            response.Body = counted;
            await _progressive.SendAsIsAsync(plan, FirstByte(delivery.Length), counted, () => file.ExecuteAsync(HttpContext), cancellationToken);
            return TypedResults.Empty;
        }

        response.Headers[DlnaFeatures] = TranscodeFeatures;

        // A renderer that asks for a time is told which one it gets, also when that is the start
        var seek = DlnaTimeSeek.Start(Request.Headers[DlnaTimeSeek.Header].ToString(), plan.DurationMs);
        if (seek is { } asked)
        {
            response.Headers[DlnaTimeSeek.Header] = DlnaTimeSeek.Range(asked, plan.DurationMs);
        }

        response.ContentType = ProgressiveService.ContentType;
        try
        {
            await _progressive.StreamAsync(plan, seek ?? 0, response.Body, cancellationToken);
            return TypedResults.Empty;
        }
        catch (TranscodeException exception) when (!response.HasStarted)
        {
            var statusCode = exception.Failure == TranscodeFailure.Busy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status500InternalServerError;
            return TypedResults.Problem(title: "Not transcoded", detail: exception.Message, statusCode: statusCode);
        }
        catch (TranscodeException)
        {
            // Once bytes went out there is no problem to answer with: the stream simply ends where it got to
            return TypedResults.Empty;
        }
    }

    // Where the bytes sent start: a renderer asks for the file from a byte on, or for its last bytes
    private long FirstByte(long length)
    {
        var range = Request.GetTypedHeaders().Range?.Ranges.FirstOrDefault();
        return range switch
        {
            { From: { } from } => Math.Min(from, length),
            { To: { } last } => Math.Max(0, length - last),
            _ => 0
        };
    }

    private static ProblemHttpResult NotPlayable() =>
        TypedResults.Problem(title: "Not playable", detail: "The file or the user of this url is gone.", statusCode: StatusCodes.Status404NotFound);
}
