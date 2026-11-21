using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using Akira.Server.Webhooks;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/webhooks")]
[Tags("Webhooks")]
public sealed class WebhookController : ApiController
{
    private readonly IWebhookService _webhooks;

    // Radarr and Sonarr send a few KB. A season pack with every episode in it stays far below this.
    public const long MaxWebhookBytes = 1024 * 1024;

    public WebhookController(IWebhookService webhooks)
    {
        _webhooks = webhooks;
    }

    [HttpPost("{instanceKey}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Webhook)]
    [RequestSizeLimit(MaxWebhookBytes)]
    [EndpointName("ReceiveArrWebhook")]
    [EndpointSummary("Receives a Radarr or Sonarr webhook and refreshes the movie or series it is about.")]
    [EndpointDescription("Set up as a Webhook connection in Radarr or Sonarr, with the instance's WebhookSecret as the password.")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public Results<Accepted<WebhookReceipt>, ProblemHttpResult> ReceiveArrWebhook(string instanceKey, [FromBody] JsonElement body)
    {
        var outcome = _webhooks.Receive(instanceKey, Request.Headers.Authorization, body);
        switch (outcome.Rejection)
        {
            case WebhookRejection.UnknownInstance:
                return TypedResults.Problem(title: "No webhook", detail: outcome.Problem, statusCode: StatusCodes.Status404NotFound);
            case WebhookRejection.Unauthorized:
                Response.Headers.WWWAuthenticate = "Basic realm=\"akira\"";
                return TypedResults.Problem(title: "Unauthorized", detail: outcome.Problem, statusCode: StatusCodes.Status401Unauthorized);
            case WebhookRejection.Malformed:
                return TypedResults.Problem(title: "Not a webhook", detail: outcome.Problem, statusCode: StatusCodes.Status400BadRequest);
            default:
                return TypedResults.Accepted((string?)null, new WebhookReceipt(outcome.EventType!, outcome.Queued));
        }
    }
}
