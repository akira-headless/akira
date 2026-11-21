using Akira.Core.Playback;
using Akira.Infrastructure.Transcoding;
using Xunit;

namespace Akira.Infrastructure.Tests.Transcoding;

public class FfmpegHlsArgumentsTests
{
    private static readonly TranscodingOptions Options = new() { SegmentLength = TimeSpan.FromSeconds(6) };

    [Fact]
    public void ARemux_CopiesBothStreams_AndStartsFromTheSegmentAskedFor()
    {
        var job = Job(new StreamDecision(true, AudioOutput.Copy, null, false, [StreamDecision.ContainerReason]));

        var arguments = string.Join(' ', FfmpegHlsArguments.Build(job, Options, 10, "/tmp/s"));

        Assert.Contains("-ss 60 -protocol_whitelist file -i file:/movies/Heat.mkv -copyts", arguments);
        Assert.Contains("-map 0:0 -c:v copy -map 0:2 -c:a copy", arguments);
        Assert.Contains("-start_number 10", arguments);
        Assert.DoesNotContain("libx264", arguments);
    }

    [Fact]
    public void AProgressiveStream_IsOneMpegTsOnStdout_WithTheSameDecisionAndNoProgressInTheWay()
    {
        var job = Job(new StreamDecision(false, AudioOutput.Aac, 1080, true, [StreamDecision.ContainerReason]));

        var arguments = string.Join(' ', FfmpegHlsArguments.BuildProgressive(job, Options, 754.5));

        Assert.Contains("-ss 754.5 -protocol_whitelist file -i file:/movies/Heat.mkv", arguments);
        Assert.Contains("tonemap", arguments);
        Assert.Contains("scale=-2:1080", arguments);
        Assert.Contains("-c:a aac", arguments);
        Assert.EndsWith("-f mpegts pipe:1", arguments);
        Assert.DoesNotContain("-progress", arguments);
        Assert.DoesNotContain("hls", arguments);
    }

    [Fact]
    public void AnEncode_ToneMapsScalesAndPutsAKeyframeOnEverySegmentFromWhereItStarts()
    {
        var job = Job(new StreamDecision(false, AudioOutput.Ac3, 1080, true, [StreamDecision.HdrReason]), channels: 8);

        var arguments = FfmpegHlsArguments.Build(job, Options, 3, "/tmp/s");

        var filters = arguments[arguments.ToList().IndexOf("-vf") + 1];
        Assert.StartsWith("zscale=t=linear", filters);
        Assert.EndsWith("scale=-2:1080,format=yuv420p", filters);
        Assert.Equal("expr:gte(t,n_forced*6)", arguments[arguments.ToList().IndexOf("-force_key_frames") + 1]);
        Assert.Contains("-c:a ac3 -b:a 640k -ac 6", string.Join(' ', arguments));
    }

    [Fact]
    public void AtFilmRate_TheKeyframesOfARun_AreMovedBackByHowLateItsFirstFrameIs()
    {
        var job = Job(new StreamDecision(false, AudioOutput.Aac, null, false, [StreamDecision.ContainerReason])) with { FrameRate = 24000.0 / 1001 };

        // Segment 7 starts at 42 s and its first frame, 1007, 0.29 ms later; segment 20 at 120 s on frame 2878, 36.6 ms later.
        // Each moved back 0.1 ms more, for rounding.
        var arguments = FfmpegHlsArguments.Build(job, Options, 7, "/tmp/s").ToList();
        var later = FfmpegHlsArguments.Build(job, Options, 20, "/tmp/s").ToList();

        Assert.Equal("expr:gte(t,n_forced*6-0.000392)", arguments[arguments.IndexOf("-force_key_frames") + 1]);
        Assert.Equal("expr:gte(t,n_forced*6-0.036683)", later[later.IndexOf("-force_key_frames") + 1]);
    }

    [Theory]
    [InlineData(HardwareAcceleration.Nvenc, "-hwaccel cuda -protocol_whitelist file -i", "-c:v h264_nvenc")]
    [InlineData(HardwareAcceleration.Qsv, "-hwaccel qsv -protocol_whitelist file -i", "-c:v h264_qsv")]
    [InlineData(HardwareAcceleration.Amf, "-hwaccel d3d11va -protocol_whitelist file -i", "-c:v h264_amf")]
    [InlineData(HardwareAcceleration.Vaapi, "-init_hw_device vaapi=va:/dev/dri/renderD128 -hwaccel vaapi -hwaccel_device va -filter_hw_device va -protocol_whitelist file -i",
        "format=nv12,hwupload -c:v h264_vaapi")]
    public void AGpu_DecodesAndEncodes_TheFiltersStayOnTheCpu(HardwareAcceleration hardware, string input, string encoder)
    {
        var job = Job(new StreamDecision(false, AudioOutput.Aac, 720, true, [StreamDecision.HdrReason]));

        var arguments = string.Join(' ', FfmpegHlsArguments.Build(job, Options, 0, "/tmp/s", hardware));

        Assert.Contains(input + " file:/movies/Heat.mkv", arguments);
        Assert.Contains(encoder, arguments);
        Assert.Contains("scale=-2:720,format=nv12", arguments);
        Assert.Contains("-force_key_frames expr:gte(t,n_forced*6)", arguments);
    }

    [Fact]
    public void AGpu_ToneMapsWithOpenCl_UnderTheClientsBitrate_AndTheCpuTakesOverWithoutIt()
    {
        var job = Job(new StreamDecision(false, AudioOutput.Aac, null, true, [StreamDecision.HdrReason], MaxBitrateKbps: 8_000));
        var options = new TranscodingOptions { SegmentLength = TimeSpan.FromSeconds(6), ToneMapping = ToneMapping.OpenCl };

        var gpu = string.Join(' ', FfmpegHlsArguments.Build(job, options, 0, "/tmp/s", HardwareAcceleration.Amf));
        var cpu = string.Join(' ', FfmpegHlsArguments.Build(job, options, 0, "/tmp/s"));

        Assert.Contains("-hwaccel d3d11va -init_hw_device opencl=ocl -filter_hw_device ocl -protocol_whitelist file -i", gpu);
        Assert.Contains("-vf format=p010le,hwupload,tonemap_opencl=", gpu);
        Assert.Contains("-maxrate 7744k", gpu);
        Assert.Contains("-vf zscale=t=linear", cpu);
        Assert.DoesNotContain("opencl", cpu);
    }

    [Fact]
    public void ACopiedVideo_NeverTouchesTheGpu()
    {
        var job = Job(new StreamDecision(true, AudioOutput.Aac, null, false, [StreamDecision.ContainerReason]));

        Assert.DoesNotContain("-hwaccel", FfmpegHlsArguments.Build(job, Options, 0, "/tmp/s", HardwareAcceleration.Nvenc));
    }

    [Fact]
    public void AnImageSubtitle_IsOverlaidAfterToneMapping_ScaledToThePicture()
    {
        var job = Job(new StreamDecision(false, AudioOutput.Aac, 1080, true, [StreamDecision.SubtitleReason])) with
        {
            Subtitle = new BurnedSubtitle(true, 5, null)
        };

        var arguments = FfmpegHlsArguments.Build(job, Options, 2, "/tmp/s").ToList();

        var graph = arguments[arguments.IndexOf("-filter_complex") + 1];
        Assert.StartsWith("[0:0]zscale=t=linear", graph);
        Assert.Contains("[base];[0:5][base]scale2ref=w=main_w:h=main_h[sub][picture];[picture][sub]overlay=eof_action=pass,scale=-2:1080,format=yuv420p[video]", graph);
        Assert.Equal("[video]", arguments[arguments.IndexOf("-filter_complex") + 3]);
        Assert.DoesNotContain("-vf", arguments);
    }

    [Fact]
    public void AnExternalImageSubtitle_IsASecondInput_SeekedLikeTheVideo()
    {
        var job = Job(new StreamDecision(false, AudioOutput.Aac, null, false, [StreamDecision.SubtitleReason])) with
        {
            Subtitle = new BurnedSubtitle(true, null, "/movies/Heat.sup")
        };

        var arguments = string.Join(' ', FfmpegHlsArguments.Build(job, Options, 3, "/tmp/s"));

        Assert.Contains("-ss 18 -protocol_whitelist file -i file:/movies/Heat.mkv -ss 18 -protocol_whitelist file -i file:/movies/Heat.sup", arguments);
        Assert.Contains("[1:0][base]scale2ref", arguments);
    }

    [Fact]
    public void ATextSubtitle_IsRenderedByLibass_FromAPathEscapedTwice()
    {
        var job = Job(new StreamDecision(false, AudioOutput.Aac, null, false, [StreamDecision.SubtitleReason])) with
        {
            Subtitle = new BurnedSubtitle(false, null, @"E:\Series\Anne Rice's [x], y.en.srt")
        };

        var arguments = FfmpegHlsArguments.Build(job, Options, 0, "/tmp/s").ToList();

        // As in ffmpeg's filtergraph docs: the option escapes \ ' and :, the graph then escapes \ ' [ ] , and ;
        Assert.Equal(@"E\\:/Series/Anne Rice\\\'s \[x\]\, y.en.srt", FfmpegHlsArguments.FilterPath(job.Subtitle.FilePath!));
        Assert.Equal(@"subtitles=filename=E\\:/Series/Anne Rice\\\'s \[x\]\, y.en.srt,format=yuv420p", arguments[arguments.IndexOf("-vf") + 1]);
    }

    [Fact]
    public void CopiedVideo_OnKeyframes_StartsOnItsKeyframe_AndCutsWhereTheLayoutSays()
    {
        var segments = HlsSegments.FromKeyframes([0, 11.006, 12.526, 25.006, 33.046], 40_000, TimeSpan.FromSeconds(6));
        var job = Job(new StreamDecision(true, AudioOutput.Aac, null, false, [StreamDecision.AudioCodecReason])) with { Segments = segments };

        var arguments = FfmpegHlsArguments.Build(job, Options, 1, "/tmp/s", run: "r1").ToList();

        // Past the 3/23 s ffmpeg seeks back for B-frames, short of the next keyframe; the cuts count from the run's keyframe
        Assert.Equal("11.136935", arguments[arguments.IndexOf("-ss") + 1]);
        Assert.Equal("14,22.04", arguments[arguments.IndexOf("-segment_times") + 1]);
        Assert.Equal("1", arguments[arguments.IndexOf("-segment_start_number") + 1]);
        Assert.Equal(Path.Combine("/tmp/s", "%d.r1.part"), arguments[^1]);
        Assert.DoesNotContain("-hls_time", arguments);
    }

    [Fact]
    public void FfmpegProgress_MeasuresThePaceBetweenReports_AlsoAfterASeek()
    {
        var progress = new FfmpegProgress();
        var start = DateTimeOffset.UnixEpoch;

        // A run started at 720 seconds: ffmpeg 7.1 counts from its first output, already 11 seconds in at the first report
        Report(progress, start, frame: 0, outTimeUs: 0);
        var first = Report(progress, start.AddSeconds(2), frame: 100, outTimeUs: 11_000_000);
        var second = Report(progress, start.AddSeconds(4), frame: 250, outTimeUs: 17_000_000);

        Assert.Null(first);
        Assert.Equal((3.0, 75.0), (second!.Speed, second.Fps));
    }

    private static TranscodeSample? Report(FfmpegProgress progress, DateTimeOffset at, int frame, long outTimeUs)
    {
        progress.Read($"frame={frame}", at);
        progress.Read($"out_time_us={outTimeUs}", at);
        progress.Read("speed=999x", at);
        return progress.Read("progress=continue", at);
    }

    private static TranscodeJob Job(StreamDecision decision, int channels = 2) =>
        new(Guid.NewGuid(), "/movies/Heat.mkv", 0, 2, channels, decision, 600_000);
}
