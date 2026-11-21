using System.Globalization;
using Microsoft.Extensions.Options;
using Akira.Core.Playback;
using Akira.Infrastructure.Processes;
using Akira.Infrastructure.Transcoding;

namespace Akira.Infrastructure.Trickplay;

/// <summary>
/// Makes thumbnail sheets with ffmpeg: keyframes only, one frame per interval, tiled into JPEG sheets.
/// </summary>
public sealed class TrickplayGenerator : ITrickplayGenerator
{
    // Raise when the sheets change, so every file is made again
    public const int Version = 1;

    private readonly IOptions<TrickplayOptions> _options;
    private readonly IOptions<TranscodingOptions> _transcoding;

    public TrickplayGenerator(
        IOptions<TrickplayOptions> options,
        IOptions<TranscodingOptions> transcoding)
    {
        _options = options;
        _transcoding = transcoding;
    }

    /// <inheritdoc />
    public async Task<TrickplayLayout> GenerateAsync(string videoPath, int? videoWidth, int? videoHeight, long durationMs, string folder,
        CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var intervalMs = (int)settings.Interval.TotalMilliseconds;
        var height = TrickplayLayout.HeightFor(settings.Width, videoWidth, videoHeight);
        var layout = new TrickplayLayout(intervalMs, settings.Width, height, settings.Columns, settings.Rows,
            TrickplayLayout.CountFor(durationMs, intervalMs));

        // Made next to the old sheets and swapped in when done, so a player never sees half a set
        var partial = folder + ".partial";
        DeleteFolder(partial);
        Directory.CreateDirectory(partial);
        try
        {
            await RunAsync(Arguments(videoPath, layout, partial), settings.Timeout, cancellationToken);

            var sheets = Directory.GetFiles(partial, "*.jpg").Length;
            if (sheets == 0)
            {
                throw new TrickplayException(TrickplayFailure.Failed, "ffmpeg made no thumbnails.");
            }

            DeleteFolder(folder);
            Directory.Move(partial, folder);
            // A duration a little longer than the video leaves the last sheet short
            return layout with { Count = Math.Min(layout.Count, sheets * layout.PerSheet) };
        }
        finally
        {
            DeleteFolder(partial);
        }
    }

    public static IReadOnlyList<string> Arguments(string videoPath, TrickplayLayout layout, string folder) =>
    [
        "-nostdin", "-hide_banner", "-v", "error",
        // Decoding keyframes only is many times faster than every frame, and close enough for a seek preview
        "-skip_frame", "nokey",
        "-protocol_whitelist", "file", "-i", "file:" + videoPath,
        "-map", "0:v:0", "-an", "-sn", "-dn",
        "-vf", string.Create(CultureInfo.InvariantCulture,
            $"fps=1000/{layout.IntervalMs},scale={layout.Width}:{layout.Height},tile={layout.Columns}x{layout.Rows}"),
        "-q:v", "5",
        "-f", "image2", "-start_number", "0",
        Path.Combine(folder, "%d.jpg")
    ];

    private async Task RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var settings = _transcoding.Value;
        ProcessResult result;
        try
        {
            // A thumbnail run is the lowest priority work on the server
            result = await ProcessRunner.RunAsync(new ProcessRun(settings.FfmpegPath, arguments, timeout, BelowNormalPriority: true), cancellationToken);
        }
        catch (ProcessRunException exception) when (exception.Failure == ProcessRunFailure.NotStarted)
        {
            throw new TrickplayException(TrickplayFailure.NotInstalled, settings.FfmpegMissing(), exception);
        }
        catch (ProcessRunException exception)
        {
            throw new TrickplayException(TrickplayFailure.Failed, $"ffmpeg took longer than {timeout} and was stopped.", exception);
        }

        if (result.ExitCode != 0)
        {
            var detail = result.Error.Length == 0 ? $"exit code {result.ExitCode}" : result.Error;
            throw new TrickplayException(TrickplayFailure.Failed, "ffmpeg could not make thumbnails: " + detail);
        }
    }

    private static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
