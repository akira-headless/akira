namespace Akira.Infrastructure.Images;

public sealed class ImageCacheOptions
{
    public const string SectionName = "ImageCache";

    // Relative to the working directory like the SQLite database, so it lives in the config volume
    public string Path { get; set; } = "config/images";

    // 0 turns the cache off
    public int MaxMegabytes { get; set; } = 500;

    public string ResolvePath() => System.IO.Path.GetFullPath(Path);
}
