using Akira.Server.Models.Health;

namespace Akira.Server.Interfaces.Services;

public interface IHealthService
{
    /// <summary>
    /// Retrieves the server version and whether the database is reachable.
    /// </summary>
    Task<HealthResponse> CheckAsync(CancellationToken cancellationToken);
}
