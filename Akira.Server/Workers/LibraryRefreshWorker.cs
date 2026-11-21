using Akira.Server.Interfaces.Services;
using Akira.Server.Webhooks;

namespace Akira.Server.Workers;

/// <summary>
/// Refreshes the movies and series that webhooks and actions named: a webhook's after a short wait, an action's at once.
/// </summary>
public sealed class LibraryRefreshWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LibraryRefreshQueue _queue;
    private readonly TimeProvider _time;
    private readonly ILogger<LibraryRefreshWorker> _logger;

    // Sonarr sends one Download per episode of a season pack, within seconds. Waiting a moment
    // turns the whole pack into one refresh of the series instead of one per episode.
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    public LibraryRefreshWorker(
        IServiceScopeFactory scopeFactory,
        LibraryRefreshQueue queue,
        TimeProvider time,
        ILogger<LibraryRefreshWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _queue.Reader.WaitToReadAsync(stoppingToken))
        {
            // After an action someone is looking at the result
            if (!_queue.TakeUrgent())
            {
                await Task.Delay(Debounce, _time, stoppingToken);
            }

            var batch = new HashSet<LibraryRefresh>();
            while (_queue.Reader.TryRead(out var refresh))
            {
                batch.Add(refresh);
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var refresher = scope.ServiceProvider.GetRequiredService<ILibraryRefreshService>();
                await refresher.RefreshAsync(batch, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lost for now, the next full sync catches up
                _logger.LogError(exception, "Refreshing {Count} items failed", batch.Count);
            }
        }
    }
}
