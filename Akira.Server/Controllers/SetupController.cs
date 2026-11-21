using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/setup")]
[Tags("Setup")]
[AllowAnonymous]
public sealed class SetupController : ApiController
{
    private readonly ISetupService _service;

    public SetupController(ISetupService service)
    {
        _service = service;
    }

    [HttpGet]
    [EndpointName("GetSetupStatus")]
    [EndpointSummary("Tells whether the first admin still has to be created.")]
    public Ok<SetupStatus> GetSetupStatus() => TypedResults.Ok(_service.GetStatus());

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    [EndpointName("CompleteSetup")]
    [EndpointSummary("Creates the first admin with the setup code from the server log, and returns an API key for it.")]
    public async Task<Results<Created<SetupResponse>, ProblemHttpResult>> CompleteSetup(SetupRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CompleteAsync(request, cancellationToken);
        return result.Succeeded
            ? TypedResults.Created("/api/v1/users/me", result.Value)
            : ServiceResults.Problem(result, HttpContext);
    }
}
