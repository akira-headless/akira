using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Subtitles;
using Akira.Infrastructure.Transcoding;

namespace Akira.Server.Workers;

/// <summary>
/// Stops transcodes nobody watches and removes what a previous run of the server left in the temp folder.
/// </summary>
public partial class TranscodeCleanupWorker : BackgroundService
{
    private readonly IHlsTranscoder _transcoder;
    private readonly IOptions<TranscodingOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<TranscodeCleanupWorker> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SubtitleLifetime = TimeSpan.FromDays(1);

    public TranscodeCleanupWorker(
        IHlsTranscoder transcoder,
        IOptions<TranscodingOptions> options,
        TimeProvider time,
        ILogger<TranscodeCleanupWorker> logger)
    {
        _transcoder = transcoder;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Try(RemoveLeftovers);

        using var timer = new PeriodicTimer(Interval, _time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _transcoder.StopIdle();
            Try(RemoveOldSubtitles);
        }
    }

    // A temp folder that cannot be listed must not stop the server, which an exception leaving a worker does
    private void Try(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not clean the transcode folder: {Message}", exception.Message);
        }
    }

    // The temp folder can be one the admin picked, so only the folders this server makes are removed
    private void RemoveLeftovers()
    {
        var root = _options.Value.ResolveTempPath();
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name == SubtitleConverter.Folder || SessionFolder().IsMatch(name))
            {
                TryDelete(() => Directory.Delete(directory, recursive: true), directory);
            }
        }
    }

    private void RemoveOldSubtitles()
    {
        var folder = Path.Combine(_options.Value.ResolveTempPath(), SubtitleConverter.Folder);
        if (!Directory.Exists(folder))
        {
            return;
        }

        var cutoff = _time.GetUtcNow().UtcDateTime - SubtitleLifetime;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoff)
            {
                TryDelete(() => File.Delete(file), file);
            }
        }
    }

    private void TryDelete(Action delete, string path)
    {
        try
        {
            delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not remove {Path}: {Message}", path, exception.Message);
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex SessionFolder();
}
