using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Playback;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/watch-state")]
[Tags("Watch state")]
public sealed class WatchStateController : ApiController
{
    private readonly IWatchStateImportService _service;

    public WatchStateController(IWatchStateImportService service)
    {
        _service = service;
    }

    [HttpPost("import")]
    [EndpointName("ImportWatchState")]
    [EndpointSummary("Adds watch state from another server to your own, matched on TMDB, TVDB or IMDb ids, at most 1000 entries a call. " +
                     "It never makes anything unplayed, keeps a position watched here more recently, and changes nothing with dryRun.")]
    public async Task<Results<Ok<WatchStateImportResult>, ProblemHttpResult>> ImportWatchState(WatchStateImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.ImportAsync(User.GetUserId()!.Value, request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }
}
