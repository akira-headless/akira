using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Bridges;
using Akira.Server.Endpoints;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix)]
[Tags("Links")]
public sealed class LinkController : ApiController
{
    private readonly IBridgeService _service;

    public LinkController(IBridgeService service)
    {
        _service = service;
    }

    [HttpGet("bridges")]
    [EndpointName("ListBridges")]
    [EndpointSummary("Lists the switched on bridges: the apps that connect through one, how they connect and the address to give them.")]
    public Ok<IReadOnlyList<BridgeSummary>> ListBridges()
    {
        var bridges = _service.List(ServerBase());
        return TypedResults.Ok(bridges);
    }

    [HttpPost("links")]
    [EndpointName("CreateLink")]
    [EndpointSummary("Makes a link for you and one bridge, such as an add-on url. The token is in this response only, revoke it under devices.")]
    public async Task<Results<Created<LinkCreated>, ProblemHttpResult>> CreateLink(CreateLinkRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateLinkAsync(User, request, ServerBase(), cancellationToken);
        return result.Succeeded
            ? TypedResults.Created($"{ItemRoutes.ApiPrefix}/devices/{result.Value!.Device.Id}", result.Value)
            : ServiceResults.Problem(result, HttpContext);
    }

    private Uri ServerBase() => new($"{Request.Scheme}://{Request.Host}{Request.PathBase}/");
}
