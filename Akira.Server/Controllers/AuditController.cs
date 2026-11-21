using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Actions;
using Akira.Server.Models.Items;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/audit")]
[Tags("Audit")]
[Authorize(Policy = AkiraPolicies.Admin)]
public sealed class AuditController : ApiController
{
    private readonly IActionService _service;

    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public AuditController(IActionService service)
    {
        _service = service;
    }

    [HttpGet]
    [EndpointName("ListAudit")]
    [EndpointSummary("Lists every delete, unmonitor and mark played, newest first.")]
    public async Task<Results<Ok<PagedResponse<AuditEntryModel>>, ValidationProblem>> ListAudit(
        CancellationToken cancellationToken,
        int offset = 0,
        int limit = DefaultLimit)
    {
        var errors = new Dictionary<string, string[]>();
        if (offset < 0)
        {
            errors["offset"] = ["offset cannot be negative."];
        }

        if (limit is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"limit must be between 1 and {MaxLimit}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var entries = await _service.ListAuditAsync(offset, limit, cancellationToken);
        return TypedResults.Ok(entries);
    }
}
