using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Models.Webhooks;
using Akira.Server.Services.Webhooks;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/outgoing-webhooks")]
[Tags("Outgoing webhooks")]
[Authorize(Policy = AkiraPolicies.Admin)]
public sealed class OutgoingWebhookController : ApiController
{
    private readonly IOutgoingWebhookService _service;

    public OutgoingWebhookController(IOutgoingWebhookService service)
    {
        _service = service;
    }

    [HttpGet]
    [EndpointName("ListOutgoingWebhooks")]
    [EndpointSummary("Lists the urls that get events, with how their deliveries go.")]
    public async Task<Ok<IReadOnlyList<OutgoingWebhookSummary>>> ListOutgoingWebhooks(CancellationToken cancellationToken) =>
        TypedResults.Ok(await _service.ListAsync(cancellationToken));

    [HttpPost]
    [EndpointName("CreateOutgoingWebhook")]
    [EndpointSummary("Adds a url that gets every event from now on, or the types named. The signing secret is only in this response.")]
    [EndpointDescription("""
        Each delivery is a POST of { sequence, type, version, payload, createdAt, userId } with X-Akira-Event, X-Akira-Delivery
        (the sequence) and X-Akira-Signature: t=<unix seconds>,v1=<hex HMAC-SHA256 of "t.body" with the secret>. Deliveries
        come in sequence order; a failing receiver is retried with a growing delay and later events wait behind it.
        """)]
    public async Task<Results<Created<CreatedOutgoingWebhook>, ProblemHttpResult>> CreateOutgoingWebhook(CreateOutgoingWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return result.Succeeded
            ? TypedResults.Created($"/api/v1/outgoing-webhooks/{result.Value!.Webhook.Id}", result.Value)
            : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPatch("{id:guid}")]
    [EndpointName("UpdateOutgoingWebhook")]
    [EndpointSummary("Changes the name, url, event types or whether it is on. A failing webhook is tried again right away.")]
    public async Task<Results<Ok<OutgoingWebhookSummary>, ProblemHttpResult>> UpdateOutgoingWebhook(Guid id, UpdateOutgoingWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpDelete("{id:guid}")]
    [EndpointName("DeleteOutgoingWebhook")]
    [EndpointSummary("Removes a webhook.")]
    public async Task<Results<NoContent, ProblemHttpResult>> DeleteOutgoingWebhook(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(id, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPost("{id:guid}/rotate-secret")]
    [EndpointName("RotateOutgoingWebhookSecret")]
    [EndpointSummary("Replaces the signing secret. The new one is only in this response.")]
    public async Task<Results<Ok<CreatedOutgoingWebhook>, ProblemHttpResult>> RotateOutgoingWebhookSecret(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RotateSecretAsync(id, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPost("{id:guid}/test")]
    [EndpointName("TestOutgoingWebhook")]
    [EndpointSummary("Sends a webhook.test delivery now and answers what the receiver said.")]
    public async Task<Results<Ok<OutgoingWebhookTestResult>, ProblemHttpResult>> TestOutgoingWebhook(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.TestAsync(id, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }
}
