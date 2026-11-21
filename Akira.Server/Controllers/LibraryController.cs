using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Items;
using Akira.Server.Webhooks;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/library")]
[Tags("Library")]
public sealed class LibraryController : ApiController
{
    private readonly ILibraryService _service;
    private readonly LibrarySyncTrigger _syncTrigger;

    public const int MaxShelfLength = 100;

    public LibraryController(
        ILibraryService service,
        LibrarySyncTrigger syncTrigger)
    {
        _service = service;
        _syncTrigger = syncTrigger;
    }

    [HttpGet("continue-watching")]
    [EndpointName("ContinueWatching")]
    [EndpointSummary("Lists what you started and did not finish, most recent first.")]
    public async Task<Ok<IReadOnlyList<ItemSummary>>> ContinueWatching(CancellationToken cancellationToken, int limit = 20)
    {
        var userId = User.GetUserId()!.Value;
        var shelfLength = Math.Clamp(limit, 1, MaxShelfLength);
        var items = await _service.ContinueWatchingAsync(userId, shelfLength, cancellationToken);
        return TypedResults.Ok(items);
    }

    [HttpGet("next-up")]
    [EndpointName("NextUp")]
    [EndpointSummary("Lists the next episode of every series you are watching.")]
    public async Task<Ok<IReadOnlyList<ItemSummary>>> NextUp(CancellationToken cancellationToken, int limit = 20)
    {
        var userId = User.GetUserId()!.Value;
        var shelfLength = Math.Clamp(limit, 1, MaxShelfLength);
        var items = await _service.NextUpAsync(userId, shelfLength, cancellationToken);
        return TypedResults.Ok(items);
    }

    [HttpPost("sync")]
    [EndpointName("SyncLibrary")]
    [EndpointSummary("Starts a full sync with Radarr and Sonarr, to look for new movies and series now.")]
    [EndpointDescription("Answers at once. The sync runs in the background and ends with a scan.progress event of kind sync, " +
        "after the library events of what it changed. Asking while a sync is already waiting to start queues nothing more.")]
    public Accepted<LibrarySyncReceipt> SyncLibrary() =>
        TypedResults.Accepted((string?)null, new LibrarySyncReceipt(_syncTrigger.Request()));
}
