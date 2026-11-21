using Microsoft.Extensions.Options;
using Akira.Infrastructure.Trickplay;
using Akira.Server.Interfaces.Services;

namespace Akira.Server.Workers;

/// <summary>
/// Makes thumbnail sheets one file at a time, newest first, after probing has read the files.
/// </summary>
public sealed class TrickplayWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<TrickplayOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<TrickplayWorker> _logger;

    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AfterProblem = TimeSpan.FromMinutes(10);

    public TrickplayWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<TrickplayOptions> options,
        TimeProvider time,
        ILogger<TrickplayWorker> logger)
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
            _logger.LogInformation("Trickplay thumbnails are off ({Section}:{Key})", TrickplayOptions.SectionName, nameof(TrickplayOptions.Enabled));
            return;
        }

        // The first sync and probe pass come first
        await Task.Delay(StartDelay, _time, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = TimeSpan.Zero;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var trickplay = scope.ServiceProvider.GetRequiredService<ITrickplayService>();
                if (!await trickplay.GenerateNextAsync(stoppingToken))
                {
                    await trickplay.RemoveOrphansAsync(stoppingToken);
                    wait = Idle;
                }
            }
            catch (TrickplayException exception) when (exception.Failure == TrickplayFailure.NotInstalled)
            {
                _logger.LogError("Trickplay thumbnails stopped: {Reason}", exception.Message);
                wait = AfterProblem;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Making trickplay thumbnails failed");
                wait = AfterProblem;
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, stoppingToken);
            }
        }
    }
}
