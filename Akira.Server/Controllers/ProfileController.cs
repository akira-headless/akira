using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/auth/profiles")]
[Tags("Profiles")]
[Authorize(Policy = AkiraPolicies.Device)]
public sealed class ProfileController : ApiController
{
    private readonly IProfileService _service;

    public ProfileController(IProfileService service)
    {
        _service = service;
    }

    [HttpGet]
    [EndpointName("ListProfiles")]
    [EndpointSummary("Lists the profiles this device can use.")]
    public async Task<Ok<IReadOnlyList<ProfileSummary>>> ListProfiles(CancellationToken cancellationToken) =>
        TypedResults.Ok(await _service.ListAsync(User, cancellationToken));

    [HttpPost("{userId:guid}/session")]
    [EndpointName("StartProfileSession")]
    [EndpointSummary("Picks a profile on a shared device and returns a session token for it.")]
    public async Task<Results<Ok<ProfileSession>, ProblemHttpResult>> StartProfileSession(Guid userId, StartProfileSessionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.StartSessionAsync(User, userId, request.Pin, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }
}
