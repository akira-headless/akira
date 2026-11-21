namespace Akira.Infrastructure.Transcoding;

public sealed class TranscodingOptions
{
    public const string SectionName = "Transcoding";

    // The one ffmpeg of the server: transcodes, subtitles, thumbnails, fingerprints and image hashes all start it from here
    public string FfmpegPath { get; set; } = "ffmpeg";

    // Segments and converted subtitles; what a previous run of the server left there is removed at startup. Defaults to a
    // folder in the system's temp directory, which in a container is its writable layer.
    public string? TempPath { get; set; }

    // Each one is a running ffmpeg, which encoding video keeps busy. 0 turns transcoding off.
    public int MaxConcurrent { get; set; } = 2;

    public TimeSpan SegmentLength { get; set; } = TimeSpan.FromSeconds(6);

    // Decoding and encoding on the GPU; the filters (tone mapping, scaling, subtitles) stay on the CPU. A GPU that fails
    // makes the server fall back to libx264 until it restarts.
    public HardwareAcceleration HardwareAcceleration { get; set; } = HardwareAcceleration.None;
    public ToneMapping ToneMapping { get; set; } = ToneMapping.Cpu;
    public string VaapiDevice { get; set; } = "/dev/dri/renderD128";

    // libx264's speed against size, a faster preset needs more bitrate for the same picture
    public string Preset { get; set; } = "veryfast";
    public int MaxVideoBitrateKbps { get; set; } = 20_000;

    // A transcode nobody asked a segment of for this long is stopped
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // How long a player waits for a segment that is still being made
    public TimeSpan SegmentTimeout { get; set; } = TimeSpan.FromSeconds(60);

    // ffmpeg is stopped when it is this far past the last segment a player asked for, and goes on when the player gets there
    public TimeSpan MaxAhead { get; set; } = TimeSpan.FromMinutes(3);

    // Segments this far before the last one asked for are removed. Together with MaxAhead this is what a session keeps on disk.
    public TimeSpan KeepBehind { get; set; } = TimeSpan.FromMinutes(2);

    public string FfmpegMissing() =>
        $"ffmpeg could not be started from '{FfmpegPath}'. Install ffmpeg or set {SectionName}:{nameof(FfmpegPath)}.";

    public string ResolveTempPath() =>
        string.IsNullOrWhiteSpace(TempPath) ? Path.Combine(Path.GetTempPath(), "akira-transcodes") : TempPath;
}
