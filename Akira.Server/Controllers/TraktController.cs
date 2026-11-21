using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Models.Trakt;
using Akira.Server.Services.Trakt;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/users/me/trakt")]
[Tags("Trakt")]
public sealed class TraktController : ApiController
{
    private readonly ITraktAccountService _service;

    public TraktController(ITraktAccountService service)
    {
        _service = service;
    }

    [HttpGet]
    [EndpointName("GetTraktStatus")]
    [EndpointSummary("Tells whether this server has a Trakt app, and whether your account is linked or waiting for its code.")]
    public async Task<Ok<TraktStatus>> GetTraktStatus(CancellationToken cancellationToken)
    {
        var status = await _service.GetStatusAsync(User.GetUserId()!.Value, cancellationToken);
        return TypedResults.Ok(status);
    }

    [HttpPost("link")]
    [EndpointName("StartTraktLink")]
    [EndpointSummary("Starts linking your Trakt account. Enter pending.userCode at pending.verificationUrl, then poll.")]
    public Task<Results<Ok<TraktStatus>, ProblemHttpResult>> StartTraktLink(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Answer(_service.StartLinkAsync(userId, cancellationToken));
    }

    [HttpPost("link/poll")]
    [EndpointName("PollTraktLink")]
    [EndpointSummary("Asks Trakt whether you entered the code, every pending.intervalSeconds. Linked imports your watched history.")]
    public Task<Results<Ok<TraktStatus>, ProblemHttpResult>> PollTraktLink(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId()!.Value;
        return Answer(_service.PollLinkAsync(userId, cancellationToken));
    }

    [HttpPost("import")]
    [EndpointName("ImportTraktHistory")]
    [EndpointSummary("Marks played what Trakt says you watched. It only adds plays, nothing becomes unplayed.")]
    public async Task<Results<Ok<TraktImportResult>, ProblemHttpResult>> ImportTraktHistory(CancellationToken cancellationToken)
    {
        var result = await _service.ImportAsync(User.GetUserId()!.Value, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpDelete]
    [EndpointName("UnlinkTrakt")]
    [EndpointSummary("Unlinks your Trakt account. What was imported stays.")]
    public async Task<Results<NoContent, ProblemHttpResult>> UnlinkTrakt(CancellationToken cancellationToken)
    {
        var result = await _service.UnlinkAsync(User.GetUserId()!.Value, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : ServiceResults.Problem(result, HttpContext);
    }

    private async Task<Results<Ok<TraktStatus>, ProblemHttpResult>> Answer(Task<ServiceResult<TraktStatus>> task)
    {
        var result = await task;
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }
}
