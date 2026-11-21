using Microsoft.Extensions.Options;
using Akira.Infrastructure.Probing;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Probing;

// Needs ffprobe, which the integration job installs
[Trait("Category", "Integration")]
public class FfprobeRunnerTests
{
    private static readonly string Synthetic = Path.Combine(AppContext.BaseDirectory, "Fixtures", "media", "synthetic.mkv");

    [Fact]
    public async Task GetVersion_ReportsFfprobe()
    {
        var version = await Runner().GetVersionAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("ffprobe version", version);
    }

    [Fact]
    public async Task Probe_ReadsTheSyntheticFile()
    {
        var output = await Runner().ProbeAsync(Synthetic, TestContext.Current.CancellationToken);

        Assert.Equal(8, output.Streams!.Count);
        Assert.Equal(2, output.Chapters!.Count);
        Assert.Equal("smpte2084", output.Streams[0].ColorTransfer);
    }

    [Fact]
    public async Task ProbeSubtitle_CountsEvents()
    {
        var output = await Runner().ProbeSubtitleAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "media", "sample.srt"), TestContext.Current.CancellationToken);

        Assert.Equal("12", Assert.Single(output.Streams!).NbReadPackets);
    }

    [Fact]
    public async Task Probe_UnreadableFile_IsFailedWithFfprobesMessage()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"not-media-{Guid.NewGuid():N}.mkv");
        await File.WriteAllTextAsync(path, "this is not a video", TestContext.Current.CancellationToken);
        try
        {
            var exception = await Assert.ThrowsAsync<ProbeException>(
                () => Runner().ProbeAsync(path, TestContext.Current.CancellationToken));

            Assert.Equal(ProbeFailure.Failed, exception.Failure);
            Assert.Contains("exited with", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingBinary_IsNotInstalled()
    {
        var runner = Runner(new ProbingOptions { FfprobePath = "ffprobe-that-does-not-exist" });

        var exception = await Assert.ThrowsAsync<ProbeException>(() => runner.GetVersionAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ProbeFailure.NotInstalled, exception.Failure);
        Assert.Contains("Probing:FfprobePath", exception.Message);
    }

    private static FfprobeRunner Runner(ProbingOptions? options = null)
    {
        options ??= new ProbingOptions();
        if (options.FfprobePath == "ffprobe")
        {
            options.FfprobePath = Ffprobe.Path;
        }

        return new FfprobeRunner(Options.Create(options));
    }
}
