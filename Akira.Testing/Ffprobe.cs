namespace Akira.Testing;

/// <summary>
/// Locates ffprobe for tests: FFPROBE_PATH when set, otherwise ffprobe on the PATH.
/// </summary>
public static class Ffprobe
{
    public static string Path => Environment.GetEnvironmentVariable("FFPROBE_PATH") is { Length: > 0 } path ? path : "ffprobe";
}
