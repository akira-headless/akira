using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Files;
using Akira.Server.Models.Updates;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + ClientUpdateRoutes.ClientUpdates)]
[Tags("Client updates")]
// A shared TV that has not picked a profile updates too
[Authorize(Policy = AkiraPolicies.Client)]
public sealed class ClientUpdateController : ApiController
{
    private readonly IClientUpdateService _updates;

    public ClientUpdateController(IClientUpdateService updates)
    {
        _updates = updates;
    }

    [HttpGet(ClientUpdateRoutes.Platform)]
    [EndpointName("GetClientUpdate")]
    [EndpointSummary("Retrieves the newest build of an app (android, linux or what the server configures) the server holds, without asking its source. 404 when there is none.")]
    public Results<Ok<ClientUpdateModel>, ProblemHttpResult> GetClientUpdate(string platform) =>
        _updates.Get(platform) is { } update ? TypedResults.Ok(update) : NoUpdate(platform);

    [HttpPost(ClientUpdateRoutes.Check)]
    [EndpointName("CheckClientUpdate")]
    [EndpointSummary("Asks the app's update source for a newer build and downloads it, then retrieves the newest build. 503 when the source fails.")]
    public async Task<Results<Ok<ClientUpdateModel>, ProblemHttpResult>> CheckClientUpdate(string platform, CancellationToken cancellationToken)
    {
        var result = await _updates.CheckAsync(platform, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : ServiceResults.Problem(result, HttpContext);
    }

    [AcceptVerbs("GET", "HEAD", Route = ClientUpdateRoutes.File)]
    [EndpointName("DownloadClientUpdate")]
    [EndpointSummary("Sends the file of the newest build of an app, with range requests to resume.")]
    [ProducesResponseType(typeof(void), StatusCodes.Status200OK, "application/vnd.android.package-archive", "application/vnd.flatpak",
        "application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]
    public Results<PhysicalFileHttpResult, ProblemHttpResult> DownloadClientUpdate(string platform)
    {
        var delivery = _updates.Deliver(platform);
        return delivery.Status == DeliveryStatus.Ready
            ? TypedResults.PhysicalFile(delivery.Path!, delivery.ContentType, Path.GetFileName(delivery.Path), delivery.LastModified,
                new EntityTagHeaderValue(delivery.EntityTag!), enableRangeProcessing: true)
            : NoUpdate(platform);
    }

    private static ProblemHttpResult NoUpdate(string platform) =>
        TypedResults.Problem(title: "No update", detail: $"This server has no build of the {platform} app.", statusCode: StatusCodes.Status404NotFound);
}
