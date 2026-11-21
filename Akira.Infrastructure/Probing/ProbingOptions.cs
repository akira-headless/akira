namespace Akira.Infrastructure.Probing;

public sealed class ProbingOptions
{
    public const string SectionName = "Probing";

    public string FfprobePath { get; set; } = "ffprobe";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    // How often new and changed files are probed, and how often every probed file is checked
    // for changes on disk and new subtitles next to it
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan RescanInterval { get; set; } = TimeSpan.FromMinutes(30);

    public int MaxConcurrency { get; set; } = 2;
    public int BatchSize { get; set; } = 25;
}
