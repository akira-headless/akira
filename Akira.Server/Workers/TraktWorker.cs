using Akira.Server.Events;
using Akira.Infrastructure.Integrations.Trakt;
using Akira.Server.Services.Trakt;

namespace Akira.Server.Workers;

/// <summary>
/// Sends scrobbles and history to linked Trakt accounts as soon as events are stored.
/// </summary>
public sealed class TraktWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITraktClient _trakt;
    private readonly EventSignal _signal;
    private readonly ILogger<TraktWorker> _logger;

    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    public TraktWorker(
        IServiceScopeFactory scopeFactory,
        ITraktClient trakt,
        EventSignal signal,
        ILogger<TraktWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _trakt = trakt;
        _signal = signal;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_trakt.IsConfigured)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Taken before reading, so events stored while this round runs wake the next one at once
            var seen = _signal.Version;
            var more = false;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<ITraktSyncService>();
                more = await sync.SyncPendingAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Trakt sync failed");
            }

            if (!more)
            {
                await _signal.WaitAsync(seen, Poll, stoppingToken);
            }
        }
    }
}
