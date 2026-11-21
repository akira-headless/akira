using Microsoft.Extensions.Options;
using Akira.Infrastructure.Subtitles;
using Akira.Server.Interfaces.Services;
using Akira.Server.Services.Subtitles;

namespace Akira.Server.Workers;

/// <summary>
/// Works through subtitle requests as they come in, and follows running translations every poll interval.
/// </summary>
public sealed class SubtitleRequestWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SubtitleRequestTrigger _trigger;
    private readonly IOptions<SubtitleOptions> _options;
    private readonly ILogger<SubtitleRequestWorker> _logger;

    public SubtitleRequestWorker(
        IServiceScopeFactory scopeFactory,
        SubtitleRequestTrigger trigger,
        IOptions<SubtitleOptions> options,
        ILogger<SubtitleRequestWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _trigger = trigger;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<ISubtitleRequestProcessor>();
                await processor.ProcessAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Working on subtitle requests failed");
            }

            await _trigger.WaitAsync(_options.Value.PollInterval, stoppingToken);
        }
    }
}
