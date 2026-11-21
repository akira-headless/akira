using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Akira.Core.Playback;
using Akira.Infrastructure.Probing;
using Akira.Infrastructure.Subtitles;
using Akira.Infrastructure.Transcoding;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Transcoding;

[Trait("Category", "Integration")]
public class HlsTranscoderTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("akira_hls_").FullName;
    private string _video = "";

    public async ValueTask InitializeAsync()
    {
        // 20 seconds of H.264 with FLAC in Matroska: a browser takes neither the container nor the audio
        _video = Path.Combine(_directory, "input.mkv");
        await Run(Ffmpeg.Path, "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "20", "-c:v", "libx264", "-preset", "ultrafast", "-g", "250", "-c:a", "flac", _video);
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ASeek_RestartsWhereThePlayerAsks_AndEverySegmentStartsWhereThePlaylistSaysItDoes()
    {
        using var transcoder = Transcoder();
        var job = new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, new StreamDecision(false, AudioOutput.Aac, null, false, ["audio-codec"]), 20_000);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Starting in the middle, then a seek back to the start, which is before where ffmpeg began
        var seeked = await transcoder.GetSegmentAsync(job, 6, cancellationToken);
        var afterSeek = await transcoder.GetSegmentAsync(job, 7, cancellationToken);
        Assert.InRange(transcoder.Status(job.SessionId)!.EncodedToMs!.Value, 16_000, 20_000);
        var first = await transcoder.GetSegmentAsync(job, 0, cancellationToken);
        var second = await transcoder.GetSegmentAsync(job, 1, cancellationToken);

        Assert.Equal(0, await StartSeconds(first), 1);
        Assert.Equal(2, await StartSeconds(second), 1);
        Assert.Equal(12, await StartSeconds(seeked), 1);
        Assert.Equal(14, await StartSeconds(afterSeek), 1);
        Assert.Equal(2, await DurationSeconds(seeked), 1);
        Assert.InRange(transcoder.Running, 0, 1);
        Assert.Equal("libx264", transcoder.Status(job.SessionId)!.Encoder);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(seeked)!, "*.tmp"));

        // Stop returns at once; ffmpeg ends and the folder goes right after. A player resuming at that moment keeps its segments.
        transcoder.Stop(job.SessionId);
        var resumed = await transcoder.GetSegmentAsync(job, 2, cancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Directory.Exists(Path.GetDirectoryName(seeked)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        Assert.False(Directory.Exists(Path.GetDirectoryName(seeked)));
        Assert.True(File.Exists(resumed));
        transcoder.Stop(job.SessionId);
        deadline = DateTime.UtcNow.AddSeconds(10);
        while (transcoder.Running > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        Assert.Equal(0, transcoder.Running);
    }

    [Fact]
    public async Task At23976FramesASecond_ASegmentStartsOnTheSameFrame_WhicheverRunWritesIt()
    {
        // Film rate: no segment boundary falls on a frame, so each run has to pick the same frame after it. Segment 20
        // starts 40 ms after its boundary and segment 21 a third of a millisecond after, where a run that counts its
        // segments from its own first frame is a frame late.
        var video = Path.Combine(_directory, "film-rate.mkv");
        await Run(Ffmpeg.Path, "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24000/1001", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "47", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "flac", video);
        using var transcoder = Transcoder();
        var decision = new StreamDecision(false, AudioOutput.Aac, null, false, ["audio-codec"]);
        var fromItsOwnStart = new TranscodeJob(Guid.NewGuid(), video, 0, 1, 1, decision, 47_000, FrameRate: 24000.0 / 1001);
        var fromTheOneBefore = fromItsOwnStart with { SessionId = Guid.NewGuid() };
        var cancellationToken = TestContext.Current.CancellationToken;

        var own = await Packets(await transcoder.GetSegmentAsync(fromItsOwnStart, 21, cancellationToken), "v");
        var before = await Packets(await transcoder.GetSegmentAsync(fromTheOneBefore, 20, cancellationToken), "v");
        var passed = await Packets(await transcoder.GetSegmentAsync(fromTheOneBefore, 21, cancellationToken), "v");

        // Frame 1007 is at 42.0003 s, the first one at or after 42
        Assert.Equal(42.0, own.First, 2);
        Assert.Equal(own.First, passed.First, 3);
        Assert.Equal(own.Last, passed.Last, 3);
        // And the segment before ends on the frame before it, none twice and none missing
        Assert.Equal(1001.0 / 24000, passed.First - before.Last, 2);
    }

    [Fact]
    public async Task CopiedVideo_IsCutOnItsKeyframes_TheSameWhicheverRunWritesASegment()
    {
        // Keyframes up to 13 seconds apart and B-frames as in an x265 release, and AC-3, which a browser gets as AAC
        var video = Path.Combine(_directory, "long-gop.mkv");
        await Run(Ffmpeg.Path, "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "40", "-c:v", "libx264", "-preset", "veryfast", "-x264-params", "keyint=1000:scenecut=0:bframes=3",
            "-force_key_frames", "0,3.2,11,12.52,25,33.04", "-c:a", "ac3", video);
        var cancellationToken = TestContext.Current.CancellationToken;
        var index = new KeyframeIndex(new FfprobeRunner(Options.Create(new ProbingOptions { FfprobePath = Ffprobe.Path })),
            NullLogger<KeyframeIndex>.Instance);
        var segments = HlsSegments.FromKeyframes((await index.GetAsync(video, cancellationToken))!, 40_000, TimeSpan.FromSeconds(6));
        var job = new TranscodeJob(Guid.NewGuid(), video, 0, 1, 6, new StreamDecision(true, AudioOutput.Aac, null, false, ["audio-codec"]),
            40_000, Segments: segments);
        using var transcoder = Transcoder();

        // A seek into the middle, then back to the start: the second run passes segment 2 and writes it again
        var seeked = await transcoder.GetSegmentAsync(job, 2, cancellationToken);
        var fromSeek = await Packets(seeked, "v");
        var audioFromSeek = await Packets(seeked, "a");
        await transcoder.GetSegmentAsync(job, 0, cancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (transcoder.Status(job.SessionId) is { Running: true } && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        // The ac3 track starts 6 ms early, which -start_at_zero moves every timestamp by
        Assert.Equal([0, 11.006, 25.006, 33.046], Enumerable.Range(0, segments.Count).Select(segments.Start).Select(s => Math.Round(s, 3)));
        Assert.Equal(25.006, fromSeek.First, 3);
        Assert.Equal(fromSeek, await Packets(seeked, "v"));
        // The audio of a seek starts where ffmpeg seeked to, a little past the keyframe
        Assert.InRange(audioFromSeek.First - fromSeek.First, -0.05, 0.2);
        Assert.Equal(11.006, (await Packets(await transcoder.GetSegmentAsync(job, 1, cancellationToken), "v")).First, 3);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(seeked)!, "*.part"));
        Assert.Equal(40_000, transcoder.Status(job.SessionId)!.EncodedToMs);
    }

    [Fact]
    public async Task ARunFarAhead_IsStopped_GoesOnWhereThePlayerGetsTo_AndSegmentsBehindAreRemoved()
    {
        // Ten minutes at 5 frames a second: quick to make, and far more than a player holds
        var video = Path.Combine(_directory, "long.mkv");
        await Run(Ffmpeg.Path, "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=5", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "600", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "flac", video);
        using var transcoder = Transcoder(maxConcurrent: 1, maxAhead: TimeSpan.FromSeconds(10), keepBehind: TimeSpan.FromSeconds(4));
        var job = new TranscodeJob(Guid.NewGuid(), video, 0, 1, 1, new StreamDecision(false, AudioOutput.Aac, null, false, ["audio-codec"]), 600_000);
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = await transcoder.GetSegmentAsync(job, 0, cancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (transcoder.Status(job.SessionId) is { Running: true } && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        // Stopped long before the end, and its place is kept for when the player gets there
        var stoppedAt = transcoder.Status(job.SessionId)!.EncodedToMs!.Value;
        Assert.InRange(stoppedAt, 12_000, 300_000);
        Assert.Equal(1, transcoder.Running);

        // The last segment there is starts ffmpeg again behind it, the next one comes from that run
        var last = (int)(stoppedAt / 2000) - 1;
        await transcoder.GetSegmentAsync(job, last, cancellationToken);
        var next = await transcoder.GetSegmentAsync(job, last + 1, cancellationToken);
        Assert.Equal((last + 1) * 2, await StartSeconds(next), 1);
        Assert.False(File.Exists(first), "The first segment is far behind the player now");

        // A seek back makes a removed segment again
        Assert.Equal(0, await StartSeconds(await transcoder.GetSegmentAsync(job, 0, cancellationToken)), 1);
    }

    [Fact]
    public async Task AfterATrackChange_ASegmentThePlayerStillReads_IsNotServedWithTheOldTrack()
    {
        using var transcoder = Transcoder();
        var stereo = new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, new StreamDecision(false, AudioOutput.Aac, null, false, ["audio-codec"]), 20_000);
        var surround = stereo with { Decision = stereo.Decision with { Audio = AudioOutput.Ac3 } };
        var cancellationToken = TestContext.Current.CancellationToken;

        var before = await transcoder.GetSegmentAsync(stereo, 0, cancellationToken);
        string after;
        // Opened as the web server opens a file it sends, which on Windows keeps it from being removed
        await using (new FileStream(before, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            after = await transcoder.GetSegmentAsync(surround, 0, cancellationToken);
        }

        var codec = await Run(Ffprobe.Path, "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=codec_name", "-of", "csv=p=0", after);
        // MPEG-TS lists the stream once more under its program
        Assert.All(codec.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), name => Assert.Equal("ac3", name));
    }

    [Fact]
    public async Task CopiedVideoInMp4_IsCutOnTheKeyframesOfItsSampleTable_TheSameWhicheverRunWritesASegment()
    {
        // B-frames and an edit list, as every x264 MP4 has, and keyframes up to 13 seconds apart
        var video = Path.Combine(_directory, "long-gop.mp4");
        await Run(Ffmpeg.Path, "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24000/1001", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "40", "-c:v", "libx264", "-preset", "veryfast", "-x264-params", "keyint=1000:scenecut=0:bframes=3",
            "-force_key_frames", "0,3.2,11,12.52,25,33.04", "-c:a", "aac", video);
        var cancellationToken = TestContext.Current.CancellationToken;
        var index = new KeyframeIndex(new FfprobeRunner(Options.Create(new ProbingOptions { FfprobePath = Ffprobe.Path })),
            NullLogger<KeyframeIndex>.Instance);
        var keyframes = (await index.GetAsync(video, cancellationToken))!;
        var segments = HlsSegments.FromKeyframes(keyframes, 40_000, TimeSpan.FromSeconds(6));
        var job = new TranscodeJob(Guid.NewGuid(), video, 0, 1, 2, new StreamDecision(true, AudioOutput.Aac, null, false, ["container"]),
            40_000, Segments: segments);
        using var transcoder = Transcoder();

        var seeked = await transcoder.GetSegmentAsync(job, 2, cancellationToken);
        var fromSeek = await Packets(seeked, "v");
        await transcoder.GetSegmentAsync(job, 0, cancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (transcoder.Status(job.SessionId) is { Running: true } && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, cancellationToken);
        }

        // What ffprobe calls the keyframes, which are the frames ffmpeg can cut a copy on
        var probed = (await Run(Ffprobe.Path, "-v", "error", "-select_streams", "v:0", "-skip_frame", "nokey", "-show_entries", "frame=pts_time",
                "-of", "csv=p=0", video))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => double.Parse(line.TrimEnd(','), CultureInfo.InvariantCulture));
        Assert.Equal(probed.Select(time => Math.Round(time, 3)), keyframes.Select(time => Math.Round(time, 3)));
        Assert.Equal(4, segments.Count);
        Assert.Equal(segments.Start(2), fromSeek.First, 2);
        Assert.Equal(fromSeek, await Packets(seeked, "v"));
    }

    [Fact]
    public async Task ASegmentPastTheEnd_IsRefused_AndAFullServer_SaysSo()
    {
        using var transcoder = Transcoder(maxConcurrent: 1);
        var decision = new StreamDecision(false, AudioOutput.Aac, null, false, ["video-codec"]);
        var cancellationToken = TestContext.Current.CancellationToken;
        // Long enough that its run still holds the only place when the second session asks; 20 seconds encode in a moment
        var longer = Path.Combine(_directory, "busy.mkv");
        await Run(Ffmpeg.Path, "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-t", "180", "-c:v", "libx264", "-preset",
            "ultrafast", longer);

        var outside = await Assert.ThrowsAsync<TranscodeException>(() =>
            transcoder.GetSegmentAsync(new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, decision, 20_000), 10, cancellationToken));
        await transcoder.GetSegmentAsync(new TranscodeJob(Guid.NewGuid(), longer, 0, null, null, decision, 180_000), 0, cancellationToken);
        var busy = await Assert.ThrowsAsync<TranscodeException>(() =>
            transcoder.GetSegmentAsync(new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, decision, 20_000), 0, cancellationToken));

        Assert.Equal((TranscodeFailure.OutOfRange, TranscodeFailure.Busy), (outside.Failure, busy.Failure));
    }

    [Fact]
    public async Task AProgressiveStream_IsMpegTsFromWhereItStarts()
    {
        var hls = Transcoder();
        var progressive = new ProgressiveTranscoder(new FixedOptions(new TranscodingOptions
        {
            FfmpegPath = Ffmpeg.Path, TempPath = Path.Combine(_directory, "transcodes"), Preset = "ultrafast", MaxConcurrent = 1
        }), hls, NullLogger<ProgressiveTranscoder>.Instance);
        var job = new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, new StreamDecision(false, AudioOutput.Aac, null, false, ["video-codec"]), 20_000);
        var output = Path.Combine(_directory, "progressive.ts");

        await using (var file = File.Create(output))
        {
            await progressive.StreamAsync(job, 10, file, TestContext.Current.CancellationToken);
        }

        // Every MPEG-TS packet starts with 0x47; from second 10 there are ten seconds left
        var bytes = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal(0x47, bytes[0]);
        Assert.Equal(10, await DurationSeconds(output), 1);
        Assert.Equal(0, hls.Running);
        hls.Dispose();
    }

    [Fact]
    public async Task AProgressiveStream_OnAGpuThatFails_ComesFromLibx264_AndLaterStreamsSkipTheGpu()
    {
        using var hls = Transcoder();
        var progressive = new ProgressiveTranscoder(new FixedOptions(new TranscodingOptions
        {
            FfmpegPath = Ffmpeg.Path, Preset = "ultrafast", HardwareAcceleration = HardwareAcceleration.Vaapi,
            VaapiDevice = Path.Combine(_directory, "no-such-gpu")
        }), hls, NullLogger<ProgressiveTranscoder>.Instance);
        var job = new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, new StreamDecision(false, AudioOutput.Aac, null, false, ["video-codec"]), 20_000);
        using var output = new MemoryStream();

        await progressive.StreamAsync(job, 16, output, TestContext.Current.CancellationToken);

        Assert.True(output.Length > 0);
        Assert.True(hls.SoftwareOnly);
    }

    [Fact]
    public async Task HlsAndProgressiveRuns_ShareOneBudget()
    {
        using var transcoder = Transcoder(maxConcurrent: 1);
        var job = new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, new StreamDecision(true, AudioOutput.Aac, null, false, ["container"]), 20_000);

        // A progressive run holds the only place
        Assert.True(transcoder.TryReserve());
        var busy = await Assert.ThrowsAsync<TranscodeException>(() => transcoder.GetSegmentAsync(job, 0, TestContext.Current.CancellationToken));
        transcoder.Release();

        Assert.Equal(TranscodeFailure.Busy, busy.Failure);
        Assert.EndsWith("0.ts", await transcoder.GetSegmentAsync(job, 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMissingFfmpeg_IsNamed()
    {
        using var transcoder = Transcoder(ffmpeg: Path.Combine(_directory, "no-ffmpeg-here"));
        var job = new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, new StreamDecision(true, AudioOutput.Aac, null, false, ["container"]), 20_000);

        var exception = await Assert.ThrowsAsync<TranscodeException>(() => transcoder.GetSegmentAsync(job, 0, TestContext.Current.CancellationToken));

        Assert.Equal(TranscodeFailure.NotInstalled, exception.Failure);
        Assert.Contains("Transcoding:FfmpegPath", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ATextSubtitle_IsBurnedIn_FromAFolderWhoseNameNeedsEscaping(bool external)
    {
        // Black video with a white subtitle the whole time: only a burned subtitle makes a pixel bright
        var folder = Directory.CreateDirectory(Path.Combine(_directory, "Anne Rice's [x], y")).FullName;
        var subtitle = Path.Combine(folder, "episode.en.srt");
        await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:20,000\nWHITE TEXT HERE\n", TestContext.Current.CancellationToken);
        var video = Path.Combine(folder, "episode.mkv");
        await Run(Ffmpeg.Path, "-v", "error", "-f", "lavfi", "-i", "color=black:size=320x180:rate=25", "-i", subtitle, "-t", "20",
            "-c:v", "libx264", "-preset", "ultrafast", "-c:s", "srt", video);
        // An embedded one is drawn from its copy out of the video, as HlsService hands it over
        var burned = subtitle;
        if (!external)
        {
            var options = Options.Create(new TranscodingOptions { FfmpegPath = Ffmpeg.Path, TempPath = Path.Combine(_directory, "temp") });
            using var converter = new SubtitleConverter(new FfmpegSubtitleExtractor(options), options);
            var embedded = new SubtitleSource("episode-1", video, 1, "subrip");
            burned = await converter.BurnableAsync(embedded, [embedded], TestContext.Current.CancellationToken);
        }

        using var transcoder = Transcoder();
        var job = new TranscodeJob(Guid.NewGuid(), video, 0, null, null,
            new StreamDecision(false, AudioOutput.Aac, null, false, [StreamDecision.SubtitleReason]), 20_000,
            new BurnedSubtitle(false, null, burned));

        var first = await transcoder.GetSegmentAsync(job, 0, TestContext.Current.CancellationToken);
        var seeked = await transcoder.GetSegmentAsync(job, 6, TestContext.Current.CancellationToken);

        Assert.True(await BrightestAsync(first) > 200, "The subtitle is in the first segment");
        Assert.True(await BrightestAsync(seeked) > 200, "and still after a seek");
    }

    [Fact]
    public async Task AGpuThatFails_FallsBackToLibx264_AndTheSegmentStillComes()
    {
        using var transcoder = Transcoder(hardware: HardwareAcceleration.Vaapi, vaapiDevice: Path.Combine(_directory, "no-such-gpu"));
        var job = new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, new StreamDecision(false, AudioOutput.Aac, null, false, ["video-codec"]), 20_000);

        var segment = await transcoder.GetSegmentAsync(job, 0, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(segment));
        Assert.True(transcoder.SoftwareOnly);
    }

    // Set AKIRA_TEST_HWACCEL to Nvenc, Qsv, Vaapi or Amf on a machine with that GPU
    [Fact]
    public async Task TheGpuOfThisMachine_Encodes()
    {
        if (!Enum.TryParse<HardwareAcceleration>(Environment.GetEnvironmentVariable("AKIRA_TEST_HWACCEL"), out var hardware))
        {
            Assert.Skip("AKIRA_TEST_HWACCEL is not set.");
        }

        using var transcoder = Transcoder(hardware: hardware);
        var job = new TranscodeJob(Guid.NewGuid(), _video, 0, 1, 1, new StreamDecision(false, AudioOutput.Aac, 180, false, ["video-codec"]), 20_000,
            FrameRate: 25);

        var first = await transcoder.GetSegmentAsync(job, 0, TestContext.Current.CancellationToken);
        var seeked = await transcoder.GetSegmentAsync(job, 7, TestContext.Current.CancellationToken);

        Assert.False(transcoder.SoftwareOnly, "The GPU encoded without falling back");
        Assert.Equal(14, await StartSeconds(seeked), 1);
        Assert.Equal(2, await DurationSeconds(first), 1);
    }

    private static async Task<int> BrightestAsync(string segment)
    {
        var frame = Path.ChangeExtension(segment, ".gray");
        await Run(Ffmpeg.Path, "-v", "error", "-i", segment, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "gray", "-y", frame);
        return (await File.ReadAllBytesAsync(frame)).Max();
    }

    private HlsTranscoder Transcoder(int maxConcurrent = 2, string? ffmpeg = null, HardwareAcceleration hardware = HardwareAcceleration.None,
        string vaapiDevice = "/dev/dri/renderD128", TimeSpan? maxAhead = null, TimeSpan? keepBehind = null) =>
        new(new FixedOptions(new TranscodingOptions
        {
            FfmpegPath = ffmpeg ?? Ffmpeg.Path, TempPath = Path.Combine(_directory, "transcodes"), SegmentLength = TimeSpan.FromSeconds(2),
            Preset = "ultrafast", MaxConcurrent = maxConcurrent, HardwareAcceleration = hardware, VaapiDevice = vaapiDevice,
            MaxAhead = maxAhead ?? TimeSpan.FromMinutes(3), KeepBehind = keepBehind ?? TimeSpan.FromMinutes(2)
        }), TimeProvider.System, NullLogger<HlsTranscoder>.Instance);

    private static async Task<double> StartSeconds(string segment)
    {
        var output = await Run(Ffprobe.Path, "-v", "error", "-show_entries", "format=start_time", "-of", "csv=p=0", segment);
        return double.Parse(output.Trim(), CultureInfo.InvariantCulture);
    }

    // The first and last packet time of a segment's video (v) or audio (a)
    private static async Task<(double First, double Last)> Packets(string segment, string stream)
    {
        var output = await Run(Ffprobe.Path, "-v", "error", "-select_streams", stream + ":0", "-show_entries", "packet=pts_time", "-of", "csv=p=0",
            segment);
        var times = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => double.Parse(line.TrimEnd(','), CultureInfo.InvariantCulture)).ToList();
        return (times.Min(), times.Max());
    }

    private static async Task<double> DurationSeconds(string segment)
    {
        var output = await Run(Ffprobe.Path, "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", segment);
        return double.Parse(output.Trim(), CultureInfo.InvariantCulture);
    }

    private static async Task<string> Run(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }

    private sealed class FixedOptions(TranscodingOptions options) : IOptionsMonitor<TranscodingOptions>
    {
        public TranscodingOptions CurrentValue => options;

        public TranscodingOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<TranscodingOptions, string?> listener) => null;
    }
}
