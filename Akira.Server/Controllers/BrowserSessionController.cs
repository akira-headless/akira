using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/auth/browser-session")]
[Tags("Browser session")]
public sealed class BrowserSessionController : ApiController
{
    private readonly IBrowserSessionService _service;

    public BrowserSessionController(IBrowserSessionService service)
    {
        _service = service;
    }

    [HttpPost]
    [EndpointName("StartBrowserSession")]
    [EndpointSummary("Signs this browser in: sets an HttpOnly session cookie for a personal device, from an API key or a paired device token.")]
    public async Task<Results<Ok<CurrentUser>, ProblemHttpResult>> StartBrowserSession(BrowserSessionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.StartAsync(User, request, cancellationToken);
        if (!result.Succeeded)
        {
            return ServiceResults.Problem(result, HttpContext);
        }

        var session = result.Value!;
        Response.Cookies.Append(BrowserSessions.CookieName, session.Token, BrowserSessions.CookieOptions(Request, session.ExpiresAt));
        return TypedResults.Ok(session.User);
    }

    [HttpDelete]
    [EndpointName("EndBrowserSession")]
    [EndpointSummary("Signs this browser out: revokes its device and clears the cookie.")]
    public async Task<NoContent> EndBrowserSession(CancellationToken cancellationToken)
    {
        await _service.EndAsync(User, cancellationToken);
        Response.Cookies.Delete(BrowserSessions.CookieName, BrowserSessions.CookieOptions(Request, DateTimeOffset.UnixEpoch));
        return TypedResults.NoContent();
    }
}
