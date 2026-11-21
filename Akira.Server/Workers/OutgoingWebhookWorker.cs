using Akira.Server.Events;
using Akira.Server.Services.Webhooks;

namespace Akira.Server.Workers;

/// <summary>
/// Delivers stored events to outgoing webhooks, each in a loop of its own, woken as soon as events are stored and
/// every ten seconds for retries.
/// </summary>
public sealed class OutgoingWebhookWorker : BackgroundService
{
    // A slow receiver holds one of these while the others go on; the cap keeps a long list from opening a
    // connection and a database scope per webhook at once
    private const int MaxConcurrentDeliveries = 4;

    // As long as the first retry of a delivery waits, so that retry is not late
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EventSignal _signal;
    private readonly ILogger<OutgoingWebhookWorker> _logger;
    private readonly SemaphoreSlim _deliveries = new(MaxConcurrentDeliveries);

    public OutgoingWebhookWorker(
        IServiceScopeFactory scopeFactory,
        EventSignal signal,
        ILogger<OutgoingWebhookWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new Dictionary<Guid, Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var seen = _signal.Version;
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IOutgoingWebhookDispatcher>();
                    var enabled = await dispatcher.ListEnabledAsync(stoppingToken);

                    foreach (var id in loops.Where(loop => loop.Value.IsCompleted).Select(loop => loop.Key).ToList())
                    {
                        loops.Remove(id);
                    }

                    // A webhook enabled again or added since gets its loop here; a loop ends with its webhook
                    foreach (var id in enabled.Where(id => !loops.ContainsKey(id)))
                    {
                        loops[id] = RunAsync(id, stoppingToken);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Listing outgoing webhooks failed");
                }

                await _signal.WaitAsync(seen, Poll, stoppingToken);
            }
        }
        finally
        {
            await Task.WhenAll(loops.Values);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        _deliveries.Dispose();
    }

    // Each webhook keeps its own strict order and its own scope, since a DbContext is not thread-safe
    private async Task RunAsync(Guid webhookId, CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            // Taken before reading, so events stored while this round runs wake the next one at once
            var seen = _signal.Version;
            var round = OutgoingWebhookRound.Wait;
            try
            {
                await _deliveries.WaitAsync(stoppingToken);
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<IOutgoingWebhookDispatcher>();
                    round = await dispatcher.DeliverAsync(webhookId, stoppingToken);
                }
                finally
                {
                    _deliveries.Release();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Delivering to outgoing webhook {Id} failed", webhookId);
            }

            if (round == OutgoingWebhookRound.Stop)
            {
                return;
            }

            if (round == OutgoingWebhookRound.Wait)
            {
                try
                {
                    await _signal.WaitAsync(seen, Poll, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
