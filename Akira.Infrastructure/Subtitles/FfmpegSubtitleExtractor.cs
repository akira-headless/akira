using System.Globalization;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Processes;
using Akira.Infrastructure.Transcoding;

namespace Akira.Infrastructure.Subtitles;

public sealed class FfmpegSubtitleExtractor : ISubtitleExtractor
{
    // ffmpeg reads the whole video for its subtitles: a remux of 80 GB on a slow share takes most of an hour
    private static readonly TimeSpan MinTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TimeoutPerGigabyte = TimeSpan.FromMinutes(1);

    private readonly IOptions<TranscodingOptions> _transcoding;

    public FfmpegSubtitleExtractor(IOptions<TranscodingOptions> transcoding)
    {
        _transcoding = transcoding;
    }

    /// <inheritdoc />
    public Task ExtractAsync(string videoPath, int streamIndex, string targetPath, CancellationToken cancellationToken) =>
        ExtractAsync(videoPath, [new SubtitleExtraction(streamIndex, targetPath)], cancellationToken);

    /// <inheritdoc />
    public async Task ExtractAsync(string videoPath, IReadOnlyList<SubtitleExtraction> tracks, CancellationToken cancellationToken)
    {
        var settings = _transcoding.Value;
        var limit = TimeoutFor(videoPath);
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(new ProcessRun(settings.FfmpegPath, Arguments(videoPath, tracks), limit), cancellationToken);
        }
        catch (ProcessRunException exception) when (exception.Failure == ProcessRunFailure.NotStarted)
        {
            throw new SubtitleFileException(settings.FfmpegMissing(), exception);
        }
        catch (ProcessRunException exception)
        {
            Delete(tracks);
            throw new SubtitleFileException($"ffmpeg did not finish extracting the subtitle within {limit.TotalMinutes:0} minutes.", exception);
        }
        catch (OperationCanceledException)
        {
            Delete(tracks);
            throw;
        }

        if (result.ExitCode != 0)
        {
            Delete(tracks);
            var detail = result.Error.Length == 0 ? $"exit code {result.ExitCode}" : result.Error;
            throw new SubtitleFileException("ffmpeg could not extract the subtitle: " + detail);
        }
    }

    /// <summary>
    /// Builds the arguments that write every track to its own file from one read of the video.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string videoPath, IReadOnlyList<SubtitleExtraction> tracks)
    {
        var arguments = new List<string> { "-nostdin", "-v", "error", "-y", "-protocol_whitelist", "file", "-i", "file:" + videoPath };
        foreach (var track in tracks)
        {
            arguments.AddRange(["-map", "0:" + track.StreamIndex.ToString(CultureInfo.InvariantCulture)]);
            var extension = Path.GetExtension(track.TargetPath).ToLowerInvariant();
            if (extension == ".mks")
            {
                // With the fonts attached to the video, for the subtitles filter to load
                arguments.AddRange(["-map", "0:t?", "-c", "copy", "-f", "matroska", track.TargetPath]);
                continue;
            }

            // ASS keeps its styling, everything else becomes SRT
            arguments.AddRange(["-c:s", extension == ".srt" ? "srt" : "copy", track.TargetPath]);
        }

        return arguments;
    }

    private static TimeSpan TimeoutFor(string videoPath)
    {
        try
        {
            var gigabytes = new FileInfo(videoPath).Length / (1024.0 * 1024 * 1024);
            return MinTimeout + TimeoutPerGigabyte * gigabytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return MinTimeout;
        }
    }

    private static void Delete(IReadOnlyList<SubtitleExtraction> tracks)
    {
        foreach (var track in tracks)
        {
            SubtitlePlacement.TryDelete(track.TargetPath);
        }
    }
}
