using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using Akira.Core.Enum;
using Akira.Infrastructure.Integrations;
using Akira.Server.Models.Files;
using Akira.Server.Models.Items;
using Akira.Server.Services;

namespace Akira.Server.Endpoints;

/// <summary>
/// Shapes the answers that send a file, a subtitle, an image or an HLS part, the same for the API's routes and for
/// signed urls.
/// </summary>
internal static class MediaResults
{
    public const string HlsPlaylistType = "application/vnd.apple.mpegurl";

    // Artwork is for signed in callers: the browser may keep it, a shared cache may not
    private const string ImageCacheControl = "private, max-age=86400";

    public static Results<PhysicalFileHttpResult, ProblemHttpResult> Video(FileDelivery delivery, Guid id, HttpContext httpContext)
    {
        if (delivery.Status == DeliveryStatus.NotFound)
        {
            return TypedResults.Problem(title: "File not found", detail: $"File {id} does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (delivery.Status != DeliveryStatus.Ready)
        {
            return NotOnDisk(delivery);
        }

        httpContext.Response.Headers.CacheControl = "private, no-transform";
        return TypedResults.PhysicalFile(delivery.Path!, delivery.ContentType, lastModified: delivery.LastModified,
            entityTag: new EntityTagHeaderValue(delivery.EntityTag!), enableRangeProcessing: true);
    }

    // Only the name, never the folder: the layout of the server's disks is not for every caller
    public static Results<PhysicalFileHttpResult, ProblemHttpResult> Download(FileDelivery delivery, Guid id, HttpContext httpContext)
    {
        var video = Video(delivery, id, httpContext);
        return video.Result is PhysicalFileHttpResult file
            ? TypedResults.PhysicalFile(file.FileName, file.ContentType, Path.GetFileName(file.FileName), file.LastModified, file.EntityTag,
                enableRangeProcessing: true)
            : video;
    }

    public static Results<PhysicalFileHttpResult, ProblemHttpResult> Subtitle(FileDelivery delivery, Guid id, string streamId) =>
        delivery.Status switch
        {
            DeliveryStatus.Ready => TypedResults.PhysicalFile(delivery.Path!, delivery.ContentType),
            DeliveryStatus.NotFound => TypedResults.Problem(title: "Subtitle not found", detail: $"File {id} has no external subtitle {streamId}.",
                statusCode: StatusCodes.Status404NotFound),
            _ => NotOnDisk(delivery)
        };

    public static Results<PhysicalFileHttpResult, ProblemHttpResult> WebVtt(FileDelivery delivery, Guid id, string streamId) =>
        delivery.Status switch
        {
            DeliveryStatus.Ready => TypedResults.PhysicalFile(delivery.Path!, delivery.ContentType),
            DeliveryStatus.NotFound => TypedResults.Problem(title: "Subtitle not found", detail: $"File {id} has no text subtitle {streamId}.",
                statusCode: StatusCodes.Status404NotFound),
            DeliveryStatus.Failed => TypedResults.Problem(title: "Not converted", detail: delivery.Detail,
                statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => NotOnDisk(delivery)
        };

    public static Results<FileContentHttpResult, StatusCodeHttpResult, ProblemHttpResult> Image(ImageDelivery delivery, Guid id, ImageKind kind,
        HttpContext httpContext)
    {
        // Weak: the same picture may come from another instance at another quality when the first is away
        var tag = delivery.Tag is { } value ? new EntityTagHeaderValue($"\"{value}\"", isWeak: true) : null;
        switch (delivery.Status)
        {
            case DeliveryStatus.NotModified:
                httpContext.Response.Headers.ETag = tag!.ToString();
                httpContext.Response.Headers.CacheControl = ImageCacheControl;
                return TypedResults.StatusCode(StatusCodes.Status304NotModified);
            case DeliveryStatus.Ready:
                httpContext.Response.Headers.CacheControl = ImageCacheControl;
                return TypedResults.File(delivery.Image!.Content, delivery.Image.ContentType, entityTag: tag);
            case DeliveryStatus.Failed:
                var status = delivery.Failure switch
                {
                    UpstreamFailure.NotFound => StatusCodes.Status404NotFound,
                    UpstreamFailure.NotConfigured => StatusCodes.Status503ServiceUnavailable,
                    UpstreamFailure.Timeout => StatusCodes.Status504GatewayTimeout,
                    _ => StatusCodes.Status502BadGateway
                };
                return TypedResults.Problem(title: "Image unavailable", detail: delivery.Detail, statusCode: status);
            default:
                return TypedResults.Problem(title: "Image not found", detail: $"Item {id} has no {kind} image.",
                    statusCode: StatusCodes.Status404NotFound);
        }
    }

    // The opaque tags a client says it holds, weak or strong alike
    public static IReadOnlyCollection<string> HeldTags(HttpRequest request) =>
        request.GetTypedHeaders().IfNoneMatch.Select(tag => tag.Tag.ToString().Trim('"')).ToHashSet(StringComparer.Ordinal);

    public static Results<ContentHttpResult, ProblemHttpResult> Playlist(ServiceResult<string> result, HttpContext httpContext)
    {
        if (!result.Succeeded)
        {
            return ServiceResults.Problem(result, httpContext);
        }

        // The segments behind it change with a track change, so a player always asks again
        httpContext.Response.Headers.CacheControl = "no-cache";
        return TypedResults.Text(result.Value!, HlsPlaylistType);
    }

    public static Results<PhysicalFileHttpResult, ProblemHttpResult> Segment(ServiceResult<string> result, HttpContext httpContext)
    {
        if (!result.Succeeded)
        {
            return ServiceResults.Problem(result, httpContext);
        }

        httpContext.Response.Headers.CacheControl = "private, max-age=3600";
        return TypedResults.PhysicalFile(result.Value!, "video/mp2t");
    }

    // Not the path itself: the layout of the server's disks is not for every caller, the probe logs it
    private static ProblemHttpResult NotOnDisk(FileDelivery delivery) =>
        TypedResults.Problem(title: "Not on disk",
            detail: $"The file is not on this server's disk. Check the path mappings of instance {delivery.Detail}.",
            statusCode: StatusCodes.Status404NotFound);
}
