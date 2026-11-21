using System.Globalization;
using System.Text;
using Akira.Core.Playback;

namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Builds the ffmpeg arguments that write the segments of a version from one segment onwards.
/// </summary>
public static class FfmpegHlsArguments
{
    // HDR to SDR, needs an ffmpeg built with zimg, which the common static builds are
    private const string ToneMap =
        "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv";

    private const string VaapiName = "va";
    private const string OpenClName = "ocl";

    // On the GPU: the frames go up as 10 bit, come back tone mapped as NV12, and the rest of the chain stays as it is
    private const string OpenClToneMap =
        "format=p010le,hwupload,tonemap_opencl=tonemap=hable:desat=0:t=bt709:m=bt709:p=bt709:r=tv:format=nv12,hwdownload,format=nv12";
    private const string VaapiToneMap = "format=p010le,hwupload,tonemap_vaapi=format=nv12:t=bt709:m=bt709:p=bt709,hwdownload,format=nv12";

    private const double ProgressiveKeyframes = 2;

    // A run on keyframes seeks just past its keyframe, so rounding never lands it on the one before; also from the start,
    // where a run without a seek moves its timestamps. For video with B-frames ffmpeg seeks Matroska 3/23 s earlier than
    // asked, so the seek also goes that much further when the next keyframe allows, which lands on the keyframe either way.
    private const double SeekPastKeyframe = 0.0005;
    private const double DtsSeekBack = 3.0 / 23;

    // The segment muxer counts its cut times from the first video packet, the keyframe; a cut is taken this much early,
    // still far short of any next keyframe
    private const double CutTolerance = 0.005;

    // Frame times are compared on the grid of the frame rate; these only absorb floating point, far under one frame
    private const double GridTolerance = 1e-6;
    private const double KeyframeTolerance = 0.0001;

    public const string PartExtension = ".part";

    /// <summary>
    /// Returns the video rate an encode aims under, in kbit/s: the server's limit, or less when the client takes less, leaving room
    /// for the audio next to it.
    /// </summary>
    public static int VideoBitrateKbps(StreamDecision decision, TranscodingOptions options)
    {
        if (decision.MaxBitrateKbps is not { } client)
        {
            return options.MaxVideoBitrateKbps;
        }

        var audio = decision.Audio == AudioOutput.Ac3 ? 640 : 192;
        return Math.Min(options.MaxVideoBitrateKbps, Math.Max(500, client - audio - 64));
    }

    /// <summary>
    /// Builds the arguments for one run. hardware is the acceleration to use; the transcoder passes None after a GPU failed.
    /// A run cut on keyframes writes its segments as {index}.{run}.part, which the transcoder renames once complete.
    /// </summary>
    public static IReadOnlyList<string> Build(TranscodeJob job, TranscodingOptions options, int startSegment, string directory,
        HardwareAcceleration hardware = HardwareAcceleration.None, string run = "0")
    {
        var segment = options.SegmentLength.TotalSeconds;
        var progress = new[] { "-progress", "pipe:1", "-stats_period", "2" };
        if (job.CutsOnKeyframes)
        {
            return OnKeyframes(job, options, job.Segments!, startSegment, directory, progress, run);
        }

        // Progress goes to stdout every two seconds, for the dashboard; the segments go to files
        var start = startSegment * segment;
        var arguments = Common(job, options, start, segment, hardware, progress, FirstFrameAfter(start, job.FrameRate));
        arguments.AddRange([
            "-f", "hls",
            "-hls_time", Seconds(segment),
            "-hls_list_size", "0",
            "-hls_segment_type", "mpegts",
            "-hls_flags", "temp_file",
            "-start_number", Index(startSegment),
            "-hls_segment_filename", Path.Combine(directory, "%d.ts"),
            Path.Combine(directory, "ffmpeg.m3u8")
        ]);
        return arguments;
    }

    // Copied video can only be cut on a keyframe. Each run starts exactly on its segment's keyframe and cuts on the ones the
    // layout lists after it, so a segment is the same whichever run wrote it. The hls muxer cannot be told where to cut,
    // the segment muxer can, but it writes a segment under its final name while it is still being written.
    private static List<string> OnKeyframes(TranscodeJob job, TranscodingOptions options, HlsSegments segments, int startSegment, string directory,
        IEnumerable<string> progress, string run)
    {
        var start = segments.VideoStart(startSegment);
        var past = segments.KeyframeAfter(startSegment) - start > DtsSeekBack + 2 * SeekPastKeyframe ? DtsSeekBack + SeekPastKeyframe : SeekPastKeyframe;
        var arguments = Common(job, options, start + past, options.SegmentLength.TotalSeconds, HardwareAcceleration.None, progress, null);
        var cuts = Enumerable.Range(startSegment + 1, segments.Count - startSegment - 1).Select(index => Seconds(segments.Start(index) - start))
            .ToList();
        arguments.AddRange([
            "-f", "segment",
            "-segment_format", "mpegts",
            // Without a cut left, one past the end: with no times the muxer cuts every two seconds
            "-segment_times", cuts.Count > 0 ? string.Join(',', cuts) : Seconds(segments.Duration - start + 1),
            "-segment_time_delta", Seconds(CutTolerance),
            "-segment_start_number", Index(startSegment),
            Path.Combine(directory, $"%d.{run}{PartExtension}")
        ]);
        return arguments;
    }

    /// <summary>
    /// Builds the arguments of a progressive transcode: one MPEG-TS stream on stdout from a time onwards, for players that
    /// take a single stream (DLNA renderers). The same decision, filters and encoders as HLS, with a keyframe every two seconds.
    /// </summary>
    public static IReadOnlyList<string> BuildProgressive(TranscodeJob job, TranscodingOptions options, double startSeconds,
        HardwareAcceleration hardware = HardwareAcceleration.None)
    {
        var arguments = Common(job, options, startSeconds, ProgressiveKeyframes, hardware, [], null);
        arguments.AddRange(["-f", "mpegts", "pipe:1"]);
        return arguments;
    }

    // Everything up to the muxer. firstFrame is how far after start the first frame of the run is, null when unknown.
    private static List<string> Common(TranscodeJob job, TranscodingOptions options, double start, double segment, HardwareAcceleration hardware,
        IEnumerable<string> progress, double? firstFrame)
    {
        var encode = !job.Decision.CopyVideo && job.VideoIndex is not null;
        var gpu = encode ? hardware : HardwareAcceleration.None;
        var arguments = new List<string> { "-nostdin", "-hide_banner", "-v", "error", "-nostats" };
        arguments.AddRange(progress);

        arguments.AddRange(HardwareInput(gpu, options));
        var toneMapping = ToneMappingFor(job, gpu, options);
        if (toneMapping == ToneMapping.OpenCl)
        {
            arguments.AddRange(["-init_hw_device", "opencl=" + OpenClName, "-filter_hw_device", OpenClName]);
        }
        if (start > 0)
        {
            arguments.AddRange(["-ss", Seconds(start)]);
        }

        // A library file is read as a file and nothing else: one that is really a playlist cannot make ffmpeg fetch urls or other files
        arguments.AddRange(["-protocol_whitelist", "file", "-i", "file:" + job.InputPath]);

        // An external image subtitle is a second input, seeked the same way so its timestamps line up
        var burn = encode ? job.Subtitle : null;
        if (burn is { ImageBased: true, FilePath: { } external })
        {
            if (start > 0)
            {
                arguments.AddRange(["-ss", Seconds(start)]);
            }

            arguments.AddRange(["-protocol_whitelist", "file", "-i", "file:" + external]);
        }

        // Timestamps stay those of the file, so segment n starts at n times the segment length after any restart
        arguments.AddRange(["-copyts", "-start_at_zero", "-avoid_negative_ts", "disabled"]);

        if (job.VideoIndex is { } video)
        {
            if (encode)
            {
                arguments.AddRange(VideoFilters(job, burn, video, gpu, toneMapping));
                arguments.AddRange(Encoder(gpu, VideoBitrateKbps(job.Decision, options), options, segment, job.FrameRate, firstFrame));
            }
            else
            {
                arguments.AddRange(["-map", "0:" + Index(video), "-c:v", "copy"]);
            }
        }
        else
        {
            arguments.Add("-vn");
        }

        if (job.AudioIndex is { } audio)
        {
            arguments.AddRange(["-map", "0:" + Index(audio)]);
            arguments.AddRange(job.Decision.Audio switch
            {
                AudioOutput.Copy => ["-c:a", "copy"],
                AudioOutput.Ac3 => ["-c:a", "ac3", "-b:a", "640k", "-ac", Index(Math.Min(job.AudioChannels ?? 6, 6))],
                _ => ["-c:a", "aac", "-b:a", "192k", "-ac", "2"]
            });
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange(["-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1", "-muxdelay", "0"]);
        return arguments;
    }

    /// <summary>
    /// Escapes a path for a filter option inside a filtergraph: once for the option, once for the graph, as ffmpeg unescapes twice.
    /// </summary>
    public static string FilterPath(string path)
    {
        // Forward slashes work on Windows too and keep the escaping readable
        var option = Escape(path.Replace('\\', '/'), "\\':");
        return Escape(option, "\\'[],;");
    }

    // Decoding on the GPU; without an output format the frames come back to memory for the filters on the CPU
    private static IEnumerable<string> HardwareInput(HardwareAcceleration hardware, TranscodingOptions options) => hardware switch
    {
        HardwareAcceleration.Nvenc => ["-hwaccel", "cuda"],
        HardwareAcceleration.Qsv => ["-hwaccel", "qsv"],
        HardwareAcceleration.Vaapi => ["-init_hw_device", $"vaapi={VaapiName}:{options.VaapiDevice}", "-hwaccel", "vaapi", "-hwaccel_device", VaapiName,
            "-filter_hw_device", VaapiName],
        HardwareAcceleration.Amf => ["-hwaccel", "d3d11va"],
        _ => []
    };

    // The GPU tone maps only while it transcodes; after a GPU failure everything is on the CPU again
    private static ToneMapping ToneMappingFor(TranscodeJob job, HardwareAcceleration gpu, TranscodingOptions options)
    {
        if (!job.Decision.ToneMap || gpu == HardwareAcceleration.None)
        {
            return ToneMapping.Cpu;
        }

        if (options.ToneMapping == ToneMapping.Vaapi && gpu != HardwareAcceleration.Vaapi)
        {
            return ToneMapping.Cpu;
        }

        if (options.ToneMapping == ToneMapping.OpenCl && gpu == HardwareAcceleration.Vaapi)
        {
            return ToneMapping.Cpu;
        }

        return options.ToneMapping;
    }

    private static IEnumerable<string> VideoFilters(TranscodeJob job, BurnedSubtitle? burn, int video, HardwareAcceleration hardware, ToneMapping toneMapping)
    {
        var before = new List<string>();
        if (job.Decision.ToneMap)
        {
            before.Add(toneMapping switch
            {
                ToneMapping.OpenCl => OpenClToneMap,
                ToneMapping.Vaapi => VaapiToneMap,
                _ => ToneMap
            });
        }

        // Subtitles are drawn on the picture after tone mapping, so their white stays white, and before scaling, at the size they were made for
        var after = new List<string>();
        if (job.Decision.Height is { } height)
        {
            after.Add("scale=-2:" + Index(height));
        }

        // H.264 High is 8 bit, a 10 bit source has to come down; the GPU encoders take NV12
        after.Add(hardware == HardwareAcceleration.None ? "format=yuv420p" : "format=nv12");
        if (hardware == HardwareAcceleration.Vaapi)
        {
            after.Add("hwupload");
        }

        if (burn is { ImageBased: true })
        {
            var subtitle = burn.FilePath is not null ? "[1:0]" : $"[0:{Index(burn.StreamIndex ?? 0)}]";
            var graph = new StringBuilder($"[0:{Index(video)}]");
            graph.Append(before.Count > 0 ? string.Join(',', before) : "null").Append("[base];");
            // An image subtitle is drawn for a canvas, usually the video's size; scaled to the picture it lands where it should
            graph.Append(subtitle).Append("[base]scale2ref=w=main_w:h=main_h[sub][picture];");
            graph.Append("[picture][sub]overlay=eof_action=pass,").Append(string.Join(',', after)).Append("[video]");
            return ["-filter_complex", graph.ToString(), "-map", "[video]"];
        }

        if (burn is { ImageBased: false, FilePath: { } file })
        {
            // The subtitles filter reads the file itself; with -copyts the frames keep the times it renders at
            before.Add("subtitles=filename=" + FilterPath(file));
        }

        return ["-map", "0:" + Index(video), "-vf", string.Join(',', before.Concat(after))];
    }

    // How far after a boundary the first frame of a run is: the first frame at or after it on the video's frame grid
    private static double? FirstFrameAfter(double boundary, double? frameRate)
    {
        if (frameRate is not > 0 || boundary <= 0)
        {
            return null;
        }

        var frame = Math.Ceiling(boundary * frameRate.Value - GridTolerance);
        return Math.Max(0, frame / frameRate.Value - boundary);
    }

    private static IEnumerable<string> Encoder(HardwareAcceleration hardware, int maxrate, TranscodingOptions options, double segment, double? frameRate,
        double? firstFrame)
    {
        var rate = new[]
        {
            "-maxrate", Index(maxrate) + "k",
            "-bufsize", Index(maxrate * 2) + "k"
        };

        // A keyframe on the first frame at or after every segment boundary. ffmpeg counts t from the first frame of this
        // run, not from the file's timestamps that -copyts keeps, and at 23.976 fps that frame is up to one frame after
        // the boundary the run starts on. Each boundary is moved back by as much, so a run that started earlier cuts on
        // the same frame. A little more, so a frame exactly on a boundary is not lost to rounding.
        var keyframes = new[]
        {
            "-force_key_frames",
            firstFrame is { } offset
                ? $"expr:gte(t,n_forced*{Seconds(segment)}-{Seconds(offset + KeyframeTolerance)})"
                : $"expr:gte(t,n_forced*{Seconds(segment)})"
        };

        // The GPU encoders aim at a bitrate under the cap rather than a quality, which they do not all support the same way
        var target = new[] { "-b:v", Index(maxrate * 6 / 10) + "k" };
        IEnumerable<string> encoder = hardware switch
        {
            HardwareAcceleration.Nvenc => ["-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr", "-cq", "23", .. target, "-forced-idr", "1"],
            HardwareAcceleration.Qsv => ["-c:v", "h264_qsv", "-preset", "veryfast", .. target, "-forced_idr", "1"],
            HardwareAcceleration.Vaapi => ["-c:v", "h264_vaapi", "-rc_mode", "VBR", .. target],
            HardwareAcceleration.Amf => ["-c:v", "h264_amf", "-quality", "speed", "-rc", "vbr_peak", .. target, "-forced_idr", "1"],
            _ => ["-c:v", "libx264", "-preset", options.Preset, "-crf", "23"]
        };
        // Some GPU encoders (AMF) ignore forced keyframes and cut at their own interval; a group of pictures of one segment
        // puts their keyframes on the boundaries too
        IEnumerable<string> group = hardware != HardwareAcceleration.None && frameRate is > 0
            ? ["-g", Index((int)Math.Round(segment * frameRate.Value))]
            : [];
        return [.. encoder, "-profile:v", "high", .. rate, .. keyframes, .. group];
    }

    private static string Escape(string value, string characters)
    {
        var builder = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            if (characters.Contains(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string Index(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Seconds(double seconds) => seconds.ToString("0.######", CultureInfo.InvariantCulture);
}
