namespace Akira.Testing;

/// <summary>
/// Locates ffmpeg for tests: FFMPEG_PATH when set, otherwise ffmpeg on the PATH.
/// </summary>
public static class Ffmpeg
{
    public static string Path => Environment.GetEnvironmentVariable("FFMPEG_PATH") is { Length: > 0 } path ? path : "ffmpeg";
}
