using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;
using Xunit;

namespace Akira.Core.Tests.Playback;

public class StreamDecisionTests
{
    // What a browser plays through hls.js: MP4 and WebM, H.264, AAC, no HDR
    private static readonly DeviceCapabilities Browser = new(
        VideoCodecs: ["h264", "vp9", "av1"], Hdr: [], AudioDecode: ["aac", "mp3", "opus", "flac"], Containers: ["mp4", "webm"],
        AccessModes: ["http", "hls"], SubtitleFormats: ["vtt"]);

    private static readonly DeviceCapabilities ShieldTv = new(
        VideoCodecs: ["h264", "hevc", "av1"], Hdr: ["hdr10", "dolbyvision"], AudioPassthrough: ["ac3", "eac3", "dtshd", "truehd"],
        AudioDecode: ["aac", "flac"]);

    [Fact]
    public void WhatTheClientPlays_IsTheFileItself()
    {
        var decision = Decide(Hevc4kHdr(), Audio("truehd", 8), ShieldTv);

        Assert.True(decision.DirectPlay);
    }

    [Fact]
    public void OnlyTheContainerWrong_IsARemux()
    {
        var decision = Decide(Video("h264", 1080), Audio("aac", 2), Browser);

        Assert.Equal([StreamDecision.ContainerReason], decision.Reasons);
        Assert.True(decision.CopyVideo);
        Assert.Equal(AudioOutput.Copy, decision.Audio);
    }

    [Fact]
    public void HdrHevcInABrowser_IsEncodedAndToneMapped_AndTrueHdBecomesStereoAac()
    {
        var decision = Decide(Hevc4kHdr(), Audio("truehd", 8), Browser);

        Assert.Equal([StreamDecision.ContainerReason, StreamDecision.VideoCodecReason, StreamDecision.HdrReason, StreamDecision.AudioCodecReason],
            decision.Reasons);
        Assert.False(decision.CopyVideo);
        Assert.True(decision.ToneMap);
        Assert.Equal(AudioOutput.Aac, decision.Audio);
    }

    [Fact]
    public void Surround_StaysSurroundAsAc3_ForAClientThatPlaysAc3()
    {
        var television = ShieldTv with { AudioPassthrough = ["ac3"] };

        var decision = Decide(Video("h264", 1080), Audio("dts", 6), television);

        Assert.Equal(AudioOutput.Ac3, decision.Audio);
        Assert.True(decision.CopyVideo);
    }

    [Fact]
    public void AboveTheClientsHeight_IsScaledDown()
    {
        var decision = Decide(Video("h264", 2160), Audio("aac", 2), ShieldTv with { MaxHeight = 1080 });

        Assert.Equal((false, 1080), (decision.CopyVideo, decision.Height));
        Assert.Equal([StreamDecision.ResolutionReason], decision.Reasons);
    }

    [Fact]
    public void WhatASegmentCannotCarry_IsEncodedEvenWhenTheClientPlaysIt()
    {
        // Only the container is wrong, but neither VP9 nor FLAC goes into an MPEG-TS segment
        var decision = Decide(Video("vp9", 1080), Audio("flac", 2), Browser with { Containers = ["mp4"] }, container: "webm");

        Assert.Equal([StreamDecision.ContainerReason], decision.Reasons);
        Assert.False(decision.CopyVideo);
        Assert.Equal(AudioOutput.Aac, decision.Audio);
    }

    [Fact]
    public void NextToAVideoEncode_TheAudioIsEncodedToo_SoASeekCutsItWhereTheVideoStarts()
    {
        var decision = Decide(Video("hevc", 1080), Audio("aac", 2), Browser);

        Assert.Equal((false, AudioOutput.Aac), (decision.CopyVideo, decision.Audio));
        Assert.DoesNotContain(StreamDecision.AudioCodecReason, decision.Reasons);
    }

    [Fact]
    public void AClientThatOpensOnlyHls_GetsSegmentsOfAFileItPlays()
    {
        var decision = Decide(Video("h264", 1080), Audio("aac", 2), Browser with { Containers = null }, hlsOnly: true);

        Assert.Equal([StreamDecision.AccessModeReason], decision.Reasons);
        Assert.Equal((true, AudioOutput.Copy), (decision.CopyVideo, decision.Audio));
    }

    [Fact]
    public void AnotherAudioTrack_KeepsAnEncode_AndOnlyCopiesTheSameCodec()
    {
        var remux = new StreamDecision(true, AudioOutput.Copy, null, false, [StreamDecision.ContainerReason]);

        Assert.Equal(AudioOutput.Copy, remux.WithAudio(Audio("aac", 2), Audio("aac", 6)).Audio);
        Assert.Equal(AudioOutput.Aac, remux.WithAudio(Audio("aac", 2), Audio("ac3", 6)).Audio);
        Assert.Equal(AudioOutput.Ac3, (remux with { Audio = AudioOutput.Ac3 }).WithAudio(Audio("dts", 6), Audio("truehd", 8)).Audio);
    }

    [Fact]
    public void AnImageSubtitle_ABrowserCannotShow_IsBurnedIn_ATextOneGoesAsWebVtt()
    {
        var pgs = new MediaStream { Id = "s", Kind = StreamKind.Subtitle, Codec = "hdmv_pgs_subtitle" };
        var srt = new MediaStream { Id = "t", Kind = StreamKind.Subtitle, Codec = "subrip", IsTextBased = true };

        var burned = StreamDecision.Decide(new MediaFile { Path = "/x.mp4", InstanceKey = "radarr", Container = "mp4" }, Video("h264", 1080),
            Audio("aac", 2), Browser, subtitle: pgs);
        var sidecar = StreamDecision.Decide(new MediaFile { Path = "/x.mp4", InstanceKey = "radarr", Container = "mp4" }, Video("h264", 1080),
            Audio("aac", 2), Browser, subtitle: srt);

        Assert.Equal([StreamDecision.SubtitleReason], burned.Reasons);
        Assert.Equal((false, AudioOutput.Aac, true), (burned.CopyVideo, burned.Audio, burned.Burns(pgs)));
        Assert.True(sidecar.DirectPlay);
        Assert.False(StreamDecision.FromToken(burned.ToToken())!.Burns(srt));
    }

    [Fact]
    public void SwitchingToASubtitleThatNeedsBurning_TurnsARemuxIntoAnEncode()
    {
        var remux = Decide(Video("h264", 1080), Audio("aac", 2), Browser);
        var pgs = new MediaStream { Id = "s", Kind = StreamKind.Subtitle, Codec = "hdmv_pgs_subtitle" };

        var switched = remux.WithSubtitle(pgs);

        Assert.Equal((true, false, AudioOutput.Aac), (remux.CopyVideo, switched.CopyVideo, switched.Audio));
        Assert.Contains(StreamDecision.SubtitleReason, switched.Reasons);
        Assert.Same(remux, remux.WithSubtitle(null));
        Assert.False(Decide(Video("h264", 1080), Audio("aac", 2), ShieldTv).Burns(pgs));
    }

    [Fact]
    public void AnImageSubtitle_ThatHasToGoThroughHls_IsBurnedIn_EvenForAClientThatShowsItFromTheFile()
    {
        // The Android app: PGS from a Matroska file is fine, HDR is not
        var phone = new DeviceCapabilities(VideoCodecs: ["h264", "hevc"], Hdr: [], AudioDecode: ["aac", "ac3", "eac3"],
            SubtitleFormats: ["srt", "ass", "vtt", "pgs"], AccessModes: ["http", "hls"]);
        var pgs = new MediaStream { Id = "s", Kind = StreamKind.Subtitle, Codec = "hdmv_pgs_subtitle" };

        var direct = StreamDecision.Decide(new MediaFile { Path = "/x.mkv", InstanceKey = "radarr", Container = "mkv" }, Video("hevc", 1080),
            Audio("eac3", 6), phone, subtitle: pgs);
        var toneMapped = StreamDecision.Decide(new MediaFile { Path = "/x.mkv", InstanceKey = "radarr", Container = "mkv" }, Hevc4kHdr(),
            Audio("eac3", 6), phone, subtitle: pgs);

        Assert.True(direct.DirectPlay);
        Assert.Equal([StreamDecision.HdrReason, StreamDecision.SubtitleReason], toneMapped.Reasons);
        Assert.True(StreamDecision.FromToken(toneMapped.ToToken())!.Burns(pgs));
    }

    [Fact]
    public void AFileAboveTheClientsBitrate_IsEncodedUnderIt()
    {
        // 10 GB in an hour, about 22 Mbit/s
        var remux = new MediaFile { Path = "/x.mkv", InstanceKey = "radarr", Container = "mkv", SizeBytes = 10_000_000_000, DurationMs = 3_600_000 };

        var away = StreamDecision.Decide(remux, Video("h264", 1080), Audio("aac", 2), ShieldTv with { MaxBitrateKbps = 8_000 });
        var home = StreamDecision.Decide(remux, Video("h264", 1080), Audio("aac", 2), ShieldTv with { MaxBitrateKbps = 40_000 });

        Assert.Equal([StreamDecision.BitrateReason], away.Reasons);
        Assert.Equal((false, 8_000), (away.CopyVideo, StreamDecision.FromToken(away.ToToken())!.MaxBitrateKbps));
        Assert.True(home.DirectPlay);

        // A 4K source under the same cap comes down to 1080p, which the bitrate and a CPU both manage
        Assert.Equal(1080, StreamDecision.Decide(remux, Video("h264", 2160), Audio("aac", 2), ShieldTv with { MaxBitrateKbps = 8_000 }).Height);
    }

    [Fact]
    public void AHlsSession_KeepsTheClientsLimits_WhenATrackChanges()
    {
        var phone = new DeviceCapabilities(VideoCodecs: ["h264"], AudioDecode: ["aac", "ac3"], Containers: ["mp4"], SubtitleFormats: ["srt", "pgs"],
            AccessModes: ["http", "hls"], MaxBitrateKbps: 8_000);
        var small = new MediaFile { Path = "/x.mkv", InstanceKey = "radarr", Container = "mkv", SizeBytes = 1_000_000_000, DurationMs = 3_600_000 };
        var srt = new MediaStream { Id = "t", Kind = StreamKind.Subtitle, Codec = "subrip", IsTextBased = true };
        var pgs = new MediaStream { Id = "s", Kind = StreamKind.Subtitle, Codec = "hdmv_pgs_subtitle" };

        var remux = StreamDecision.FromToken(StreamDecision.Decide(small, Video("h264", 1080), Audio("aac", 2), phone).ToToken())!;

        // A remux that has to burn a subtitle in later still encodes under the client's cap
        Assert.Equal((true, 8_000), (remux.CopyVideo, remux.WithSubtitle(pgs).MaxBitrateKbps));
        // Text reaches HLS only as WebVTT, which this client does not show: burned in too
        Assert.True(remux.Burns(srt));
        // Another track with surround stays surround for a client that plays AC-3
        Assert.Equal(AudioOutput.Ac3, remux.WithAudio(Audio("aac", 2), Audio("truehd", 8)).Audio);
        // A stored token that repeats a name still reads
        Assert.Equal(AudioOutput.Aac, StreamDecision.FromToken("video=h264;audio=aac;audio=ac3;subs=vtt")!.Audio);
    }

    [Fact]
    public void ADecision_SurvivesTheSessionColumn()
    {
        var decision = Decide(Hevc4kHdr(), Audio("truehd", 8), Browser with { MaxHeight = 720 });

        var read = StreamDecision.FromToken(decision.ToToken())!;

        Assert.Equal((decision.CopyVideo, decision.Audio, decision.Height, decision.ToneMap), (read.CopyVideo, read.Audio, read.Height, read.ToneMap));
        Assert.Equal(decision.Reasons, read.Reasons);
        Assert.Null(StreamDecision.FromToken(null));
        Assert.Null(StreamDecision.FromToken("nonsense"));
    }

    [Fact]
    public void ThePlaylist_ListsEverySegment_TheLastOneShorter()
    {
        var playlist = HlsPlaylists.Media(HlsSegments.Uniform(14_500, TimeSpan.FromSeconds(6)), index => $"{index}.ts");

        Assert.Contains("#EXT-X-TARGETDURATION:6\n", playlist);
        Assert.Contains("#EXTINF:6.000000,\n0.ts\n#EXTINF:6.000000,\n1.ts\n#EXTINF:2.500000,\n2.ts\n#EXT-X-ENDLIST\n", playlist);
        Assert.Equal("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-STREAM-INF:BANDWIDTH=8000000,RESOLUTION=1920x1080\nmain.m3u8\n",
            HlsPlaylists.Master("main.m3u8", 8_000_000, 1920, 1080));
    }

    [Fact]
    public void CopiedVideo_IsCutOnItsKeyframes_AtLeastASegmentLengthApart()
    {
        // The keyframes around 43:00 of an x265 release, up to ten seconds apart, plus one just before the end
        double[] keyframes = [0, 3.2, 11, 12.52, 25, 33.04, 39.5];

        var segments = HlsSegments.FromKeyframes(keyframes, 40_000, TimeSpan.FromSeconds(6));
        var playlist = HlsPlaylists.Media(segments, index => $"{index}.ts");

        Assert.Equal([0, 11, 25, 33.04], Enumerable.Range(0, segments.Count).Select(segments.Start));
        Assert.Contains("#EXT-X-TARGETDURATION:14\n", playlist);
        Assert.Contains("#EXTINF:11.000000,\n0.ts\n#EXTINF:14.000000,\n1.ts\n#EXTINF:8.040000,\n2.ts\n#EXTINF:6.960000,\n3.ts\n", playlist);
    }

    private static StreamDecision Decide(MediaStream video, MediaStream audio, DeviceCapabilities capabilities, string container = "mkv",
        bool hlsOnly = false) =>
        StreamDecision.Decide(new MediaFile { Path = "/x." + container, InstanceKey = "radarr", Container = container }, video, audio, capabilities,
            hlsOnly);

    private static MediaStream Hevc4kHdr()
    {
        var video = Video("hevc", 2160);
        video.HdrFormat = HdrFormat.Hdr10;
        return video;
    }

    private static MediaStream Video(string codec, int height) =>
        new() { Id = "v", Kind = StreamKind.Video, Codec = codec, Height = height, Width = height * 16 / 9 };

    private static MediaStream Audio(string codec, int channels) =>
        new() { Id = "a", Kind = StreamKind.Audio, Codec = codec, Channels = channels };
}
