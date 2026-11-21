using Akira.Core;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Probing;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Health;

namespace Akira.Server.Services;

public class HealthService : IHealthService
{
    private readonly AkiraDbContext _dbContext;
    private readonly FfprobeStatus _ffprobe;

    public HealthService(
        AkiraDbContext dbContext,
        FfprobeStatus ffprobe)
    {
        _dbContext = dbContext;
        _ffprobe = ffprobe;
    }

    /// <inheritdoc />
    public async Task<HealthResponse> CheckAsync(CancellationToken cancellationToken)
    {
        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
        var database = canConnect ? HealthState.Healthy : HealthState.Unhealthy;

        // Without ffprobe the library still plays, files just stay unprobed, so it does not decide the status
        var probing = _ffprobe.Current.IsAvailable switch
        {
            true => HealthState.Healthy,
            false => HealthState.Unhealthy,
            null => HealthState.Unknown
        };

        return new HealthResponse(database, ServerVersion.Number, database, probing);
    }
}
