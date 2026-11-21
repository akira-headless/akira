using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Images;
using Akira.Infrastructure.Transcoding;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Images;

[Trait("Category", "Integration")]
public class ImageDecoderTests
{
    [Fact]
    public async Task AJpeg_ComesBackAsSmallRgbPixels_AndHtmlIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var decoder = new ImageDecoder(Options.Create(new TranscodingOptions { FfmpegPath = Ffmpeg.Path }));

        var pixels = await decoder.DecodeAsync(await OrangePosterAsync(cancellationToken), cancellationToken);

        Assert.Equal(ImageDecoder.Size * ImageDecoder.Size * 3, pixels.Length);
        // JPEG shifts a solid color a little; orange stays orange
        Assert.InRange(pixels[0], 235, 255);
        Assert.InRange(pixels[1], 120, 150);
        Assert.InRange(pixels[2], 0, 20);

        // What a proxy or login page sends in place of an image
        var html = Encoding.UTF8.GetBytes("<!doctype html><html><body>Sign in</body></html>");
        var refused = await Assert.ThrowsAsync<ImageDecodeException>(() => decoder.DecodeAsync(html, cancellationToken));
        Assert.Equal(ImageDecodeFailure.Failed, refused.Failure);
    }

    private static async Task<byte[]> OrangePosterAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(Ffmpeg.Path) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-hide_banner", "-v", "error", "-f", "lavfi", "-i", "color=c=0xFF8800:s=300x450", "-frames:v", "1", "-f", "image2pipe", "-c:v", "mjpeg", "pipe:1" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        using var output = new MemoryStream();
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        Assert.True(process.ExitCode == 0, await error);
        return output.ToArray();
    }
}
