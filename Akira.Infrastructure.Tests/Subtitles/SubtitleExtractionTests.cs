using System.Text;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Processes;
using Akira.Infrastructure.Subtitles;
using Akira.Infrastructure.Transcoding;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Subtitles;

[Trait("Category", "Integration")]
public class SubtitleExtractionTests
{
    [Fact]
    public async Task EmbeddedSubRip_IsWrittenToItsOwnFile()
    {
        var directory = Directory.CreateTempSubdirectory("akira_extract_").FullName;
        try
        {
            var video = Path.Combine(AppContext.BaseDirectory, "Fixtures", "media", "synthetic.mkv");
            var target = Path.Combine(directory, "synthetic.nl.srt");
            var extractor = new FfmpegSubtitleExtractor(Options.Create(new TranscodingOptions { FfmpegPath = Ffmpeg.Path }));

            // Stream 7 of the synthetic file is its Dutch SubRip track
            await extractor.ExtractAsync(video, 7, target, TestContext.Current.CancellationToken);

            var text = await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken);
            Assert.Contains("-->", text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EmbeddedSubRip_BecomesWebVttForBrowsers()
    {
        var directory = Directory.CreateTempSubdirectory("akira_extract_").FullName;
        try
        {
            var video = Path.Combine(AppContext.BaseDirectory, "Fixtures", "media", "synthetic.mkv");
            var extractor = new FfmpegSubtitleExtractor(Options.Create(new TranscodingOptions { FfmpegPath = Ffmpeg.Path }));
            using var converter = new SubtitleConverter(extractor, Options.Create(new TranscodingOptions { TempPath = directory }));
            var dutch = new SubtitleSource("synthetic-7", video, 7, "subrip");
            var english = new SubtitleSource("synthetic-4", video, 4, "subrip");

            // Two requests at once, as a player that starts over sends them, and one of them gives up
            using var leaving = new CancellationTokenSource();
            var abandoned = converter.WebVttAsync(dutch, [dutch, english], leaving.Token);
            var kept = converter.WebVttAsync(dutch, [dutch, english], TestContext.Current.CancellationToken);
            leaving.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
            var path = await kept;

            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.StartsWith("WEBVTT\n\n00:00:00.021 --> 00:00:00.061\nLine 1\n", text);
            // The same read of the video made the other track too, and the copies a burn-in draws from, and nothing else is left
            Assert.Equal(["synthetic-4.v3.srt", "synthetic-4.v3.vtt", "synthetic-7.v3.srt", "synthetic-7.v3.vtt"],
                Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName).Order());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EmbeddedAss_IsBurnedFromAFileOfItsOwn_WithTheFontsOfTheVideo()
    {
        var directory = Directory.CreateTempSubdirectory("akira_extract_").FullName;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var ass = Path.Combine(directory, "episode.ass");
            await File.WriteAllTextAsync(ass, "[Script Info]\nScriptType: v4.00+\n\n[V4+ Styles]\n" +
                "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, " +
                "ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n" +
                "Style: Default,Fansub Font,28,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1\n\n" +
                "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
                "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,Line 1\n", ct);
            var font = Path.Combine(directory, "fansub.ttf");
            await File.WriteAllBytesAsync(font, [0, 1, 0, 0], ct);
            var video = Path.Combine(directory, "episode.mkv");
            await RunAsync(Ffmpeg.Path, ["-v", "error", "-f", "lavfi", "-i", "color=black:size=64x64:rate=25", "-i", ass, "-t", "1", "-map", "0", "-map", "1",
                "-c:v", "libx264", "-preset", "ultrafast", "-c:s", "copy", "-attach", font, "-metadata:s:t", "mimetype=application/x-truetype-font", video]);
            var options = Options.Create(new TranscodingOptions { FfmpegPath = Ffmpeg.Path, TempPath = Path.Combine(directory, "temp") });
            using var converter = new SubtitleConverter(new FfmpegSubtitleExtractor(options), options);
            var embedded = new SubtitleSource("episode-1", video, 1, "ass");

            var burnable = await converter.BurnableAsync(embedded, [embedded], ct);

            Assert.EndsWith(".mks", burnable);
            var streams = await RunAsync(Ffprobe.Path, ["-v", "error", "-show_entries", "stream=codec_name,codec_type", "-of", "csv=p=0",
                burnable]);
            Assert.Equal(["ass,subtitle", "ttf,attachment"], streams.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        var result = await ProcessRunner.RunAsync(new ProcessRun(executable, arguments, TimeSpan.FromMinutes(1), MaxOutputBytes: 65_536), TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Error);
        return Encoding.UTF8.GetString(result.Output);
    }

    [Fact]
    public async Task AStreamThatIsNoSubtitle_FailsAndLeavesNoFile()
    {
        var directory = Directory.CreateTempSubdirectory("akira_extract_").FullName;
        try
        {
            var video = Path.Combine(AppContext.BaseDirectory, "Fixtures", "media", "synthetic.mkv");
            var target = Path.Combine(directory, "synthetic.nl.srt");
            var extractor = new FfmpegSubtitleExtractor(Options.Create(new TranscodingOptions { FfmpegPath = Ffmpeg.Path }));

            await Assert.ThrowsAsync<SubtitleFileException>(() => extractor.ExtractAsync(video, 0, target, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(target));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
