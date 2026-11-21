using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Transcoding;

namespace Akira.Infrastructure.Images;

/// <summary>
/// Decodes images with ffmpeg, which Akira already runs, rather than an imaging library.
/// </summary>
public sealed class ImageDecoder : IImageDecoder
{
    // Enough for a blur: the hash keeps a handful of components, more pixels only cost time
    public const int Size = 32;

    private const int MaxErrorChars = 500;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly IOptions<TranscodingOptions> _transcoding;

    public ImageDecoder(IOptions<TranscodingOptions> transcoding)
    {
        _transcoding = transcoding;
    }

    /// <inheritdoc />
    public async Task<byte[]> DecodeAsync(byte[] image, CancellationToken cancellationToken)
    {
        var ffmpeg = _transcoding.Value.FfmpegPath;
        var startInfo = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new ImageDecodeException(ImageDecodeFailure.NotInstalled,
                $"ffmpeg could not be started from '{ffmpeg}'. Install ffmpeg or set {TranscodingOptions.SectionName}:{nameof(TranscodingOptions.FfmpegPath)}.", exception);
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(Timeout);
        try
        {
            var output = ReadAsync(process.StandardOutput.BaseStream, limit.Token);
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(image, limit.Token);
            }
            catch (IOException)
            {
                // ffmpeg stopped reading, a broken image; its exit code and message say why
            }
            finally
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(limit.Token);
            var pixels = await output;
            var message = (await error).Trim();
            if (process.ExitCode != 0 || pixels.Length != Size * Size * 3)
            {
                throw new ImageDecodeException(ImageDecodeFailure.Failed,
                    "ffmpeg could not read the image: " + ErrorDetail(message, process.ExitCode, pixels.Length));
            }

            return pixels;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new ImageDecodeException(ImageDecodeFailure.Failed, $"ffmpeg took longer than {Timeout} to read an image.");
        }
    }

    // The image comes from an upstream, so ffmpeg reads it from the pipe only and may open nothing else
    public static IReadOnlyList<string> Arguments { get; } =
    [
        "-hide_banner", "-v", "error",
        "-protocol_whitelist", "pipe", "-i", "pipe:0",
        "-frames:v", "1",
        "-vf", $"scale={Size}:{Size}:flags=area",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"
    ];

    private static async Task<byte[]> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string ErrorDetail(string message, int exitCode, int bytes)
    {
        if (message.Length == 0)
        {
            return $"exit code {exitCode}, {bytes} bytes";
        }

        return message.Length > MaxErrorChars ? message[..MaxErrorChars] : message;
    }
}
