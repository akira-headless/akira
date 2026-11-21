using System.Diagnostics;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Transcoding;
using Akira.Infrastructure.Trickplay;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Trickplay;

[Trait("Category", "Integration")]
public class TrickplayGeneratorTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("akira_trickplay_").FullName;
    private string _video = "";

    public async ValueTask InitializeAsync()
    {
        // 45 seconds of 16:9 video with a keyframe every second
        _video = Path.Combine(_directory, "input.mkv");
        using var process = Process.Start(new ProcessStartInfo(Ffmpeg.Path)
        {
            ArgumentList = { "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25", "-t", "45", "-c:v", "libx264", "-preset", "ultrafast",
                "-g", "25", _video },
            UseShellExecute = false
        })!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task TheSheets_HoldOneThumbnailPerInterval_AndReplaceTheOldOnes()
    {
        var generator = Generator(new TrickplayOptions { Interval = TimeSpan.FromSeconds(5), Width = 160, Columns = 2, Rows = 2 });
        var folder = Path.Combine(_directory, "sheets");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "old.jpg"), "stale", TestContext.Current.CancellationToken);

        var layout = await generator.GenerateAsync(_video, 640, 360, 45_000, folder, TestContext.Current.CancellationToken);

        Assert.Equal((9, 160, 90, 3), (layout.Count, layout.Width, layout.Height, layout.SheetCount));
        Assert.Equal(["0.jpg", "1.jpg", "2.jpg"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        Assert.False(Directory.Exists(folder + ".partial"));
    }

    [Fact]
    public async Task NoVideo_FailsAndLeavesTheOldSheets()
    {
        var generator = Generator(new TrickplayOptions());
        var folder = Path.Combine(_directory, "kept");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "0.jpg"), "old", TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<TrickplayException>(() =>
            generator.GenerateAsync(Path.Combine(_directory, "missing.mkv"), 640, 360, 45_000, folder, TestContext.Current.CancellationToken));

        Assert.Equal(TrickplayFailure.Failed, exception.Failure);
        Assert.True(File.Exists(Path.Combine(folder, "0.jpg")));
    }

    private static TrickplayGenerator Generator(TrickplayOptions options) =>
        new(Options.Create(options), Options.Create(new TranscodingOptions { FfmpegPath = Ffmpeg.Path }));
}
