using Microsoft.Extensions.Options;
using Akira.Infrastructure.Interfaces.Sync;
using Akira.Infrastructure.Sync;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using Akira.Server.Webhooks;

namespace Akira.Server.Workers;

/// <summary>
/// Runs a full library sync at startup, then on the configured interval or earlier when a client asks for one.
/// </summary>
public sealed class LibrarySyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LibrarySyncTrigger _trigger;
    private readonly ProbeTrigger _probeTrigger;
    private readonly IOptions<LibraryOptions> _options;
    private readonly ILogger<LibrarySyncWorker> _logger;

    public LibrarySyncWorker(
        IServiceScopeFactory scopeFactory,
        LibrarySyncTrigger trigger,
        ProbeTrigger probeTrigger,
        IOptions<LibraryOptions> options,
        ILogger<LibrarySyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _trigger = trigger;
        _probeTrigger = probeTrigger;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            await SyncOnceAsync(stoppingToken);
        }
        while (await WaitAsync(stoppingToken));
    }

    private async Task<bool> WaitAsync(CancellationToken stoppingToken)
    {
        await _trigger.WaitAsync(_options.Value.SyncInterval, stoppingToken);
        return !stoppingToken.IsCancellationRequested;
    }

    private async Task SyncOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sync = scope.ServiceProvider.GetRequiredService<ILibrarySyncService>();
            var report = await sync.SyncAsync(stoppingToken);
            var events = scope.ServiceProvider.GetRequiredService<ILibraryEventService>();
            await events.PublishSyncAsync(report, ScanKind.Sync, stoppingToken);

            // New files get their tracks now instead of at the next probe interval, so they play right away
            if (report.Instances.Any(result => result.Added + result.Updated > 0))
            {
                _probeTrigger.Request();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A sync that crashes must not stop the next one from running
            _logger.LogError(exception, "Library sync failed");
        }
    }
}
