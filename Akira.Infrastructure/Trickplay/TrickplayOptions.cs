namespace Akira.Infrastructure.Trickplay;

public sealed class TrickplayOptions
{
    public const string SectionName = "Trickplay";

    public bool Enabled { get; set; } = true;

    // Where the sheets are kept, relative to the working directory like the SQLite database
    public string Path { get; set; } = "config/trickplay";

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);
    public int Width { get; set; } = 320;
    public int Columns { get; set; } = 10;
    public int Rows { get; set; } = 10;

    // A film is read keyframe by keyframe; a run longer than this is a stuck ffmpeg
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(1);

    public string ResolvePath() => System.IO.Path.GetFullPath(Path);
}
