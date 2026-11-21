using Microsoft.Extensions.Options;
using Akira.Infrastructure.Interfaces.Probing;
using Akira.Infrastructure.Probing;
using Akira.Server.Interfaces.Services;
using Akira.Server.Webhooks;

namespace Akira.Server.Workers;

/// <summary>
/// Probes new and changed files on every interval and rescans everything on the rescan interval.
/// </summary>
public sealed class ProbeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFfprobeRunner _ffprobe;
    private readonly FfprobeStatus _status;
    private readonly ProbeTrigger _trigger;
    private readonly IOptions<ProbingOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ProbeWorker> _logger;

    public ProbeWorker(
        IServiceScopeFactory scopeFactory,
        IFfprobeRunner ffprobe,
        FfprobeStatus status,
        ProbeTrigger trigger,
        IOptions<ProbingOptions> options,
        TimeProvider time,
        ILogger<ProbeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _ffprobe = ffprobe;
        _status = status;
        _trigger = trigger;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing has been probed before the first pass, so the first rescan waits a full interval
        var lastRescan = _time.GetUtcNow();
        do
        {
            try
            {
                // Inside the try with the rest: an exception that leaves a hosted service stops the server
                if (!await EnsureFfprobeAsync(stoppingToken))
                {
                    continue;
                }

                await using var scope = _scopeFactory.CreateAsyncScope();
                var probe = scope.ServiceProvider.GetRequiredService<IProbeService>();

                RescanResult? rescan = null;
                if (_time.GetUtcNow() - lastRescan >= _options.Value.RescanInterval)
                {
                    rescan = await probe.RescanAsync(stoppingToken);
                    lastRescan = _time.GetUtcNow();
                    if (rescan.Requeued + rescan.SubtitlesAdded + rescan.SubtitlesRemoved > 0)
                    {
                        _logger.LogInformation("Rescan: {Requeued} files to probe again, {Added} subtitles added, {Removed} removed",
                            rescan.Requeued, rescan.SubtitlesAdded, rescan.SubtitlesRemoved);
                    }
                }

                var result = await probe.ProbePendingAsync(stoppingToken);
                Log(result);
                var events = scope.ServiceProvider.GetRequiredService<ILibraryEventService>();
                await events.PublishProbeAsync(result, rescan, stoppingToken);
            }
            catch (ProbeException exception) when (exception.Failure == ProbeFailure.NotInstalled)
            {
                _status.Unavailable(exception.Message);
                _logger.LogError("Probing stopped: {Reason}", exception.Message);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failing pass must not stop the next one from running
                _logger.LogError(exception, "Probe pass failed");
            }
        }
        // A webhook that brought a new file wakes the worker early
        while (await WaitAsync(stoppingToken));
    }

    private async Task<bool> WaitAsync(CancellationToken stoppingToken)
    {
        await _trigger.WaitAsync(_options.Value.Interval, stoppingToken);
        return !stoppingToken.IsCancellationRequested;
    }

    private async Task<bool> EnsureFfprobeAsync(CancellationToken stoppingToken)
    {
        if (_status.Current.IsAvailable == true)
        {
            return true;
        }

        try
        {
            var version = await _ffprobe.GetVersionAsync(stoppingToken);
            _status.Available(version);
            _logger.LogInformation("Probing with {Version}", version);
            return true;
        }
        catch (ProbeException exception)
        {
            // Logged when it first fails, not again on every pass while it stays missing
            if (_status.Current.IsAvailable != false)
            {
                _logger.LogError("Files are not probed: {Reason}", exception.Message);
            }

            _status.Unavailable(exception.Message);
            return false;
        }
    }

    private void Log(ProbeRunResult result)
    {
        if (result.Probed > 0)
        {
            _logger.LogInformation("Probed {Count} files", result.Probed);
        }

        if (result.Missing > 0)
        {
            _logger.LogWarning("{Count} files were not found on disk, check the path mappings of their instances " +
                              "(GET /api/v1/files/{{id}} shows the path that was tried)", result.Missing);
        }

        if (result.Failed > 0)
        {
            _logger.LogWarning("{Count} files could not be probed", result.Failed);
        }
    }
}
