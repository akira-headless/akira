using Akira.Core.Media;

namespace Akira.Infrastructure.Subtitles;

/// <summary>
/// Places subtitle files next to a video under names the external subtitle scanner reads back.
/// </summary>
public static class SubtitlePlacement
{
    /// <summary>
    /// Returns a free name like Movie.nl.srt, or Movie.nl.2.srt when that one exists. Numbers are ignored when it is read back.
    /// </summary>
    public static string FreePath(string videoPath, string language, string extension, bool forced = false)
    {
        var directory = Path.GetDirectoryName(videoPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(videoPath);
        var code = Languages.ToTwoLetter(language) ?? language;
        var middle = forced ? code + ".forced" : code;
        extension = extension.TrimStart('.');

        for (var number = 1; ; number++)
        {
            var name = number == 1 ? $"{stem}.{middle}.{extension}" : $"{stem}.{middle}.{number}.{extension}";
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    public static async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        // Written aside and moved in, so a player or scanner never sees half a subtitle
        var temporary = path + ".part";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            File.Move(temporary, path, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw new SubtitleFileException($"Could not write {path}: {exception.Message}", exception);
        }
    }

    public static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
