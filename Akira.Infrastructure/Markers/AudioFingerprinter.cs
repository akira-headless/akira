using System.Globalization;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Processes;
using Akira.Infrastructure.Transcoding;

namespace Akira.Infrastructure.Markers;

/// <summary>
/// Fingerprints audio with ffmpeg's chromaprint muxer, which needs an ffmpeg built with libchromaprint.
/// </summary>
public sealed class AudioFingerprinter : IAudioFingerprinter
{
    // Raise when the arguments change what a fingerprint holds, so the ones kept on disk are made again
    public const int Version = 1;

    private static readonly TimeSpan MinTimeout = TimeSpan.FromMinutes(5);
    private const int MaxBytes = 4 * 1024 * 1024;

    private readonly IOptions<TranscodingOptions> _transcoding;

    public AudioFingerprinter(IOptions<TranscodingOptions> transcoding)
    {
        _transcoding = transcoding;
    }

    /// <inheritdoc />
    public async Task<uint[]> FingerprintAsync(string path, int audioIndex, TimeSpan start, TimeSpan length, CancellationToken cancellationToken)
    {
        var settings = _transcoding.Value;
        // A whole episode is read from disk, which over a slow share takes a while
        var timeout = MinTimeout + length / 2;
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(
                new ProcessRun(settings.FfmpegPath, Arguments(path, audioIndex, start, length), timeout, MaxBytes, BelowNormalPriority: true),
                cancellationToken);
        }
        catch (ProcessRunException exception) when (exception.Failure == ProcessRunFailure.NotStarted)
        {
            throw new FingerprintException(FingerprintFailure.NotSupported, settings.FfmpegMissing(), exception);
        }
        catch (ProcessRunException exception)
        {
            throw new FingerprintException(FingerprintFailure.Failed, $"ffmpeg took longer than {timeout} to fingerprint the audio.", exception);
        }

        if (result.ExitCode != 0)
        {
            // An ffmpeg without libchromaprint does not know the format at all
            if (result.Error.Contains("chromaprint", StringComparison.OrdinalIgnoreCase) && result.Error.Contains("format", StringComparison.OrdinalIgnoreCase))
            {
                throw new FingerprintException(FingerprintFailure.NotSupported,
                    "This ffmpeg has no chromaprint, so intros cannot be found by audio. The Docker image's ffmpeg has it.");
            }

            var detail = result.Error.Length == 0 ? $"exit code {result.ExitCode}" : result.Error;
            throw new FingerprintException(FingerprintFailure.Failed, "ffmpeg could not fingerprint the audio: " + detail);
        }

        if (result.OutputTruncated)
        {
            throw new FingerprintException(FingerprintFailure.Failed, "The fingerprint is larger than any stretch of audio this server asks for.");
        }

        var points = new uint[result.Output.Length / 4];
        Buffer.BlockCopy(result.Output, 0, points, 0, points.Length * 4);
        return points;
    }

    public static IReadOnlyList<string> Arguments(string path, int audioIndex, TimeSpan start, TimeSpan length) =>
    [
        "-nostdin", "-hide_banner", "-v", "error",
        "-ss", start.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
        "-t", length.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
        "-protocol_whitelist", "file", "-i", "file:" + path,
        "-map", "0:" + audioIndex.ToString(CultureInfo.InvariantCulture),
        "-ac", "2",
        // Raw is the 32 bit points themselves, little endian, nothing to decode
        "-f", "chromaprint", "-fp_format", "raw", "-"
    ];
}
