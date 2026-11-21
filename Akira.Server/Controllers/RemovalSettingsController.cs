using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Actions;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix)]
[Tags("Settings")]
public sealed class RemovalSettingsController : ApiController
{
    private readonly IRemovalSettingsService _service;

    public RemovalSettingsController(IRemovalSettingsService service)
    {
        _service = service;
    }

    [HttpGet("settings/removal")]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("GetRemovalSettings")]
    [EndpointSummary("Retrieves what removing does by default per type, and whether users other than admins may remove.")]
    public async Task<Ok<ServerRemovalSettings>> GetRemovalSettings(CancellationToken cancellationToken) =>
        TypedResults.Ok(await _service.GetServerAsync(cancellationToken));

    [HttpPut("settings/removal")]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("SetRemovalSettings")]
    [EndpointSummary("Sets what removing does by default per type, and whether users other than admins may remove.")]
    public async Task<Results<Ok<ServerRemovalSettings>, ProblemHttpResult>> SetRemovalSettings(
        ServerRemovalSettings settings,
        CancellationToken cancellationToken)
    {
        var result = await _service.SetServerAsync(settings, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpGet("users/me/settings/removal")]
    [EndpointName("GetMyRemovalSettings")]
    [EndpointSummary("Retrieves your override, what removing does for you, and whether you may remove at all.")]
    [EndpointDescription("""
        A client shows the effective behavior in its confirmation and sends the matching action: deleteAndUnmonitor is
        delete with deleteFiles and unmonitor true, deleteKeepMonitored is delete with unmonitor false and search true,
        unmonitorOnly is unmonitor.
        """)]
    public async Task<Ok<UserRemovalSettings>> GetMyRemovalSettings(CancellationToken cancellationToken) =>
        TypedResults.Ok(await _service.GetForUserAsync(User, cancellationToken));

    [HttpPut("users/me/settings/removal")]
    [EndpointName("SetMyRemovalSettings")]
    [EndpointSummary("Sets your override. A type left empty follows the server default.")]
    public async Task<Ok<UserRemovalSettings>> SetMyRemovalSettings(RemovalBehaviors behaviors, CancellationToken cancellationToken) =>
        TypedResults.Ok(await _service.SetForUserAsync(User, behaviors, cancellationToken));
}
