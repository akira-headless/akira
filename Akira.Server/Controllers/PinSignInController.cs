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

[Route(ItemRoutes.ApiPrefix + "/auth/pin")]
[Tags("Pairing")]
[AllowAnonymous]
public sealed class PinSignInController : ApiController
{
    private const string AwayFromHome = "Signing in with a PIN only works on the home network. Pair this device with a code instead.";

    private readonly IPinSignInService _service;

    public PinSignInController(IPinSignInService service)
    {
        _service = service;
    }

    [HttpGet("users")]
    [EnableRateLimiting(RateLimitPolicies.Anonymous)]
    [EndpointName("ListPinUsers")]
    [EndpointSummary("Lists who can sign in with their PIN. Only on the home network.")]
    public async Task<Results<Ok<IReadOnlyList<PinUser>>, ProblemHttpResult>> ListPinUsers(CancellationToken cancellationToken)
    {
        if (!ClientAddresses.IsAtHome(HttpContext))
        {
            return TypedResults.Problem(title: "Not at home", detail: AwayFromHome, statusCode: StatusCodes.Status403Forbidden);
        }

        var users = await _service.ListAsync(cancellationToken);
        return TypedResults.Ok(users);
    }

    [HttpPost("sign-in")]
    [EnableRateLimiting(RateLimitPolicies.PairingStart)]
    [EndpointName("SignInWithPin")]
    [EndpointSummary("Signs this device in as a user who gives their PIN, as a personal device. Only on the home network.")]
    [EndpointDescription("""
        At home is the local network directly, or through a proxy listed in Proxy:TrustedProxies that names a local client.
        Wrong PINs count towards the same lockout as on shared devices. The token answers like a completed pairing.
        """)]
    public async Task<Results<Ok<PairingCompleted>, ProblemHttpResult>> SignInWithPin(PinSignInRequest request, CancellationToken cancellationToken)
    {
        if (!ClientAddresses.IsAtHome(HttpContext))
        {
            return TypedResults.Problem(title: "Not at home", detail: AwayFromHome, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await _service.SignInAsync(request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : ServiceResults.Problem(result, HttpContext);
    }
}
