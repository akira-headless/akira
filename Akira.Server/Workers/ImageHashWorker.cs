using Akira.Infrastructure.Images;
using Akira.Server.Interfaces.Services;

namespace Akira.Server.Workers;

/// <summary>
/// Makes the blur hashes clients show while artwork loads, a batch at a time, newest items first.
/// </summary>
public sealed class ImageHashWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;
    private readonly ILogger<ImageHashWorker> _logger;

    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AfterProblem = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LongestWait = TimeSpan.FromHours(1);

    public ImageHashWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider time,
        ILogger<ImageHashWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The first sync brings the images
        await Task.Delay(StartDelay, _time, stoppingToken);
        // The images this round could not fetch stay first in line, so each batch starts past them
        var skip = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = TimeSpan.Zero;
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var hashes = scope.ServiceProvider.GetRequiredService<IImageHashService>();
                var batch = await hashes.HashNextAsync(skip, stoppingToken);
                skip += batch.Left;
                if (batch.Taken == 0)
                {
                    // The round is over, and the next one tries again what this one left
                    skip = 0;
                    wait = Idle;
                }
                else if (batch.Stopped)
                {
                    // Radarr, Sonarr or the artwork host away: the images wait for them
                    wait = batch.RetryAfter > AfterProblem ? Min(batch.RetryAfter.Value, LongestWait) : AfterProblem;
                    _logger.LogInformation("Image blurs wait {Minutes} minutes, their source does not answer", wait.TotalMinutes);
                }
            }
            catch (ImageDecodeException exception) when (exception.Failure == ImageDecodeFailure.NotInstalled)
            {
                _logger.LogWarning("Image blurs stopped: {Reason}", exception.Message);
                wait = AfterProblem;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Making image blurs failed");
                wait = AfterProblem;
            }

            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time, stoppingToken);
            }
        }
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;
}
