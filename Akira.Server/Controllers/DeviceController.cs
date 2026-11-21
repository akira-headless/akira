using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Bridges;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/devices")]
[Tags("Devices")]
public sealed class DeviceController : ApiController
{
    private readonly IDeviceService _service;

    // The device's revocation ends it, not the time: a client keeps these urls as long as the item is in its library
    private static readonly TimeSpan ArtworkLinkLifetime = TimeSpan.FromDays(3650);

    public DeviceController(IDeviceService service)
    {
        _service = service;
    }

    [HttpGet]
    [EndpointName("ListDevices")]
    [EndpointSummary("Lists your devices, or every device for an admin.")]
    public async Task<Ok<IReadOnlyList<DeviceSummary>>> ListDevices(CancellationToken cancellationToken) =>
        TypedResults.Ok(await _service.ListAsync(User, cancellationToken));

    [HttpDelete("{id:guid}")]
    [EndpointName("RevokeDevice")]
    [EndpointSummary("Revokes a device and every token it holds.")]
    public async Task<Results<NoContent, ProblemHttpResult>> RevokeDevice(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RevokeAsync(User, id, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPost(ItemRoutes.ApiPrefix + "/auth/artwork-link")]
    [Authorize(Policy = AkiraPolicies.Device)]
    [EndpointName("CreateArtworkLink")]
    [EndpointSummary("Returns a url for any item's artwork without a credential in it, for clients that store artwork urls in their own library. " +
                     "It only opens artwork and stops working when the device is revoked.")]
    public Ok<ArtworkLink> CreateArtworkLink([FromServices] SignedUrls urls)
    {
        var template = urls.ArtworkTemplate(User.GetDeviceId()!.Value, ArtworkLinkLifetime);
        return TypedResults.Ok(new ArtworkLink(template));
    }
}
