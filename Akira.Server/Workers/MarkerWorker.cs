using Microsoft.Extensions.Options;
using Akira.Infrastructure.Markers;
using Akira.Server.Services.Playback;

namespace Akira.Server.Workers;

/// <summary>
/// Compares the audio of one season at a time to find intros and credits, after probing has read the files.
/// </summary>
public sealed class MarkerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<MarkerOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<MarkerWorker> _logger;

    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AfterProblem = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NotSupported = TimeSpan.FromHours(6);

    public MarkerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MarkerOptions> options,
        TimeProvider time,
        ILogger<MarkerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Finding intros by audio is off ({Section}:{Key})", MarkerOptions.SectionName, nameof(MarkerOptions.Enabled));
            return;
        }

        await Task.Delay(StartDelay, _time, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = TimeSpan.Zero;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var scanner = scope.ServiceProvider.GetRequiredService<IMarkerScanService>();
                if (!await scanner.ScanNextSeasonAsync(stoppingToken))
                {
                    await scanner.RemoveOrphansAsync(stoppingToken);
                    wait = Idle;
                }
            }
            catch (FingerprintException exception) when (exception.Failure == FingerprintFailure.NotSupported)
            {
                _logger.LogWarning("Finding intros by audio is paused: {Reason}", exception.Message);
                wait = NotSupported;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Finding intros by audio failed");
                wait = AfterProblem;
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, stoppingToken);
            }
        }
    }
}
