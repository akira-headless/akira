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

[Route(ItemRoutes.ApiPrefix + "/auth/pairings")]
[Tags("Pairing")]
public sealed class PairingController : ApiController
{
    private readonly IPairingService _service;

    public PairingController(IPairingService service)
    {
        _service = service;
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PairingStart)]
    [EndpointName("StartPairing")]
    [EndpointSummary("Starts pairing a new device. Show the code to the user and poll for the token.")]
    public async Task<Results<Created<PairingStarted>, ProblemHttpResult>> StartPairing(StartPairingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.StartAsync(request, HttpContext.Connection.RemoteIpAddress, cancellationToken);
        return result.Succeeded
            ? TypedResults.Created($"/api/v1/auth/pairings/{result.Value!.PairingId}", result.Value)
            : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPost("{id:guid}/token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    [EndpointName("PollPairing")]
    [EndpointSummary("Polls a pairing: 202 while it waits, 200 with the device token once approved (only once).")]
    public async Task<Results<Ok<PairingCompleted>, Accepted<PairingPending>, ProblemHttpResult>> PollPairing(Guid id, PollPairingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.PollAsync(id, request.PollSecret, cancellationToken);
        if (!result.Succeeded)
        {
            return ServiceResults.Problem(result, HttpContext);
        }

        return result.Value!.Completed is { } completed
            ? TypedResults.Ok(completed)
            : TypedResults.Accepted((string?)null, result.Value.Pending!);
    }

    [HttpGet]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("ListPendingPairings")]
    [EndpointSummary("Lists devices waiting to be approved.")]
    public async Task<Ok<IReadOnlyList<PendingPairing>>> ListPendingPairings(CancellationToken cancellationToken) =>
        TypedResults.Ok(await _service.ListPendingAsync(cancellationToken));

    [HttpPost("approve")]
    [EndpointName("ApprovePairing")]
    [EndpointSummary("Approves a code as a personal device (yours, or anyone's as admin) or as a shared device (admin).")]
    public async Task<Results<Ok<DeviceSummary>, ProblemHttpResult>> ApprovePairing(ApprovePairingRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ApproveAsync(User, request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPost("deny")]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("DenyPairing")]
    [EndpointSummary("Denies a waiting code.")]
    public async Task<Results<NoContent, ProblemHttpResult>> DenyPairing(DenyPairingRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.DenyAsync(request.Code, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : ServiceResults.Problem(result, HttpContext);
    }
}
