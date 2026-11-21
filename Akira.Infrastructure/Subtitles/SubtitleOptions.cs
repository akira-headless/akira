namespace Akira.Infrastructure.Subtitles;

public sealed class SubtitleOptions
{
    public const string SectionName = "Subtitles";

    public OpenSubtitlesOptions OpenSubtitles { get; set; } = new();
    public LingarrOptions Lingarr { get; set; } = new();

    // Read until the server had one ffmpeg path, Transcoding:FfmpegPath. Kept so that a server that still sets it stops
    // at start and names the new key, and does not go on with another ffmpeg than the admin picked.
    public string? FfmpegPath { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
