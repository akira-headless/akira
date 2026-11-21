using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Processes;

namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Runs ffmpeg with its output on stdout and copies it into the response, for players that take one stream rather than
/// segments. A seek is a new request with a new start, and HLS and progressive runs share Transcoding:MaxConcurrent.
/// </summary>
public sealed class ProgressiveTranscoder : IProgressiveTranscoder
{
    private readonly IOptionsMonitor<TranscodingOptions> _options;
    private readonly IHlsTranscoder _hls;
    private readonly ILogger<ProgressiveTranscoder> _logger;

    public ProgressiveTranscoder(
        IOptionsMonitor<TranscodingOptions> options,
        IHlsTranscoder hls,
        ILogger<ProgressiveTranscoder> logger)
    {
        _options = options;
        _hls = hls;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StreamAsync(TranscodeJob job, double startSeconds, Stream output, CancellationToken cancellationToken)
    {
        var settings = _options.CurrentValue;
        if (!_hls.TryReserve())
        {
            throw new TranscodeException(TranscodeFailure.Busy,
                $"The server is already transcoding as many streams as {TranscodingOptions.SectionName}:{nameof(TranscodingOptions.MaxConcurrent)} allows.");
        }

        try
        {
            // The GPU only takes part when the video is encoded; a copy runs on the CPU whatever is configured
            var encodes = !job.Decision.CopyVideo && job.VideoIndex is not null;
            var hardware = encodes && !_hls.SoftwareOnly ? settings.HardwareAcceleration : HardwareAcceleration.None;
            var written = await RunAsync(job, startSeconds, output, settings, hardware, cancellationToken);

            // A GPU that fails before the first byte gets one more try on the CPU; after that the client has a broken stream anyway
            if (written == 0 && hardware != HardwareAcceleration.None && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("The {Hardware} transcode of session {SessionId} failed, trying libx264", hardware, job.SessionId);
                // Only when the CPU makes what the GPU could not was the GPU the problem; then no later stream pays for a failed start
                if (await RunAsync(job, startSeconds, output, settings, HardwareAcceleration.None, cancellationToken) > 0)
                {
                    _hls.FallBackToSoftware($"the {hardware} transcode of session {job.SessionId} wrote nothing");
                }
            }
        }
        finally
        {
            _hls.Release();
        }
    }

    private async Task<long> RunAsync(TranscodeJob job, double startSeconds, Stream output, TranscodingOptions settings, HardwareAcceleration hardware,
        CancellationToken cancellationToken)
    {
        using var process = Start(job, startSeconds, settings, hardware);
        _logger.LogInformation("Streaming session {SessionId} from {Start:0.#} s as MPEG-TS ({Decision}, {Hardware})", job.SessionId, startSeconds,
            job.Decision.ToToken(), job.Decision.CopyVideo ? "copy" : hardware);
        var errors = ProcessRunner.ReadErrorAsync(process.StandardError, CancellationToken.None);
        long written = 0;
        try
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // The client went away or seeked; either way this run is done
        }
        finally
        {
            await ProcessRunner.KillAsync(process);
        }

        var error = await errors;
        if (written == 0 && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("ffmpeg wrote nothing for session {SessionId}: {Error}", job.SessionId, error);
            if (hardware == HardwareAcceleration.None)
            {
                throw new TranscodeException(TranscodeFailure.Failed, $"ffmpeg could not transcode the file: {error}");
            }
        }

        return written;
    }

    private static Process Start(TranscodeJob job, double startSeconds, TranscodingOptions settings, HardwareAcceleration hardware)
    {
        try
        {
            return ProcessRunner.Start(settings.FfmpegPath, FfmpegHlsArguments.BuildProgressive(job, settings, startSeconds, hardware));
        }
        catch (ProcessRunException exception)
        {
            throw new TranscodeException(TranscodeFailure.NotInstalled, settings.FfmpegMissing(), exception);
        }
    }
}
