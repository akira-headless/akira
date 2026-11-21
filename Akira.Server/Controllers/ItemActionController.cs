using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Core.Enum;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Actions;
using ActionResult = Akira.Server.Models.Actions.ActionResult;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + ItemRoutes.Items + "/{id:guid}/actions")]
[Tags("Actions")]
public sealed class ItemActionController : ApiController
{
    private readonly IActionService _service;

    public ItemActionController(IActionService service)
    {
        _service = service;
    }

    [HttpPost("delete")]
    [EndpointName("DeleteItem")]
    [EndpointSummary("Deletes the files of a movie, episode, season or series in the named instances, and unmonitors it unless told not to.")]
    [EndpointDescription("""
        scope must be the item's own type, deleteFiles and unmonitor are both required, and instances names every
        instance that acts. search, with deleteFiles true and unmonitor false, fetches a new copy. dryRun answers what
        would happen without doing it. Answers 502 with the outcome when every instance failed. Admins only, unless
        usersMayRemove is on.
        """)]
    [ProducesResponseType<ActionResult>(StatusCodes.Status502BadGateway, "application/json")]
    public async Task<Results<Ok<ActionResult>, JsonHttpResult<ActionResult>, ProblemHttpResult>> DeleteItem(Guid id,
        DeleteActionRequest request, CancellationToken cancellationToken) =>
        Shape(await _service.DeleteAsync(User, id, request, cancellationToken));

    [HttpPost("unmonitor")]
    [EndpointName("UnmonitorItem")]
    [EndpointSummary("Unmonitors a movie, episode, season or series in the named instances and keeps its files.")]
    [ProducesResponseType<ActionResult>(StatusCodes.Status502BadGateway, "application/json")]
    public async Task<Results<Ok<ActionResult>, JsonHttpResult<ActionResult>, ProblemHttpResult>> UnmonitorItem(Guid id,
        UnmonitorActionRequest request, CancellationToken cancellationToken) =>
        Shape(await _service.UnmonitorAsync(User, id, request, cancellationToken));

    [HttpPost("mark-played")]
    [EndpointName("MarkItemPlayed")]
    [EndpointSummary("Marks an item played or unplayed for you, with every episode below a season or series.")]
    public async Task<Results<Ok<MarkPlayedResult>, ProblemHttpResult>> MarkItemPlayed(Guid id, MarkPlayedRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.MarkPlayedAsync(User, id, request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    private Results<Ok<ActionResult>, JsonHttpResult<ActionResult>, ProblemHttpResult> Shape(ServiceResult<ActionResult> result)
    {
        if (!result.Succeeded)
        {
            return ServiceResults.Problem(result, HttpContext);
        }

        // Nothing upstream did what was asked, which a script should not mistake for success
        return result.Value!.Status == ActionStatus.Failed
            ? TypedResults.Json(result.Value, statusCode: StatusCodes.Status502BadGateway)
            : TypedResults.Ok(result.Value);
    }
}
