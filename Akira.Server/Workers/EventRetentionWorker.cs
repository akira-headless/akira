using Akira.Server.Interfaces.Services;

namespace Akira.Server.Workers;

/// <summary>
/// Removes events older than the retention once an hour.
/// </summary>
public sealed class EventRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;
    private readonly ILogger<EventRetentionWorker> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public EventRetentionWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider time,
        ILogger<EventRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, _time);
        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var feed = scope.ServiceProvider.GetRequiredService<IEventFeedService>();
                var removed = await feed.PruneAsync(stoppingToken);
                if (removed > 0)
                {
                    _logger.LogDebug("Removed {Count} events past the retention", removed);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Removing old events failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
