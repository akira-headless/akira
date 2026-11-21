using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Health;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/health")]
[Tags("System")]
public sealed class HealthController : ApiController
{
    private readonly IHealthService _healthService;

    public HealthController(IHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet]
    [AllowAnonymous]
    [EndpointName("GetHealth")]
    [EndpointSummary("Retrieves the server version and database status.")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<Results<Ok<HealthResponse>, JsonHttpResult<HealthResponse>>> GetHealth(CancellationToken cancellationToken)
    {
        var health = await _healthService.CheckAsync(cancellationToken);

        return health.Status == HealthState.Healthy
            ? TypedResults.Ok(health)
            : TypedResults.Json(health, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
