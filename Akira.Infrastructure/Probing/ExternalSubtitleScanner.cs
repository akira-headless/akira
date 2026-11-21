using Akira.Core.Media;

namespace Akira.Infrastructure.Probing;

/// <summary>
/// Lists the subtitle files next to a video.
/// </summary>
public static class ExternalSubtitleScanner
{
    // Throws when the directory cannot be listed. An empty list means there really are none,
    // which would otherwise remove every external subtitle the file had.
    public static IReadOnlyList<ExternalSubtitleFile> Find(string videoPath)
    {
        var directory = Path.GetDirectoryName(videoPath);
        return string.IsNullOrEmpty(directory) ? [] : Find(videoPath, List(directory));
    }

    /// <summary>
    /// Lists the subtitle files of a folder, whichever video they belong to.
    /// </summary>
    public static IReadOnlyList<string> List(string directory) =>
        Directory.EnumerateFiles(directory).Where(path => SubtitleFiles.Extensions.Contains(Path.GetExtension(path))).ToList();

    /// <summary>
    /// Picks the subtitles of one video out of the listing of its folder, so a folder of many videos is listed once.
    /// </summary>
    public static IReadOnlyList<ExternalSubtitleFile> Find(string videoPath, IReadOnlyList<string> subtitlesInFolder)
    {
        var videoFileName = Path.GetFileName(videoPath);
        return subtitlesInFolder
            .Select(path => (Path: path, FileName: Path.GetFileName(path)))
            .Select(file => (file.Path, file.FileName, Name: SubtitleFiles.Parse(videoFileName, file.FileName)))
            .Where(file => file.Name is not null)
            .OrderBy(file => file.FileName, StringComparer.Ordinal)
            .Select(file => new ExternalSubtitleFile(file.FileName, file.Path, file.Name!))
            .ToList();
    }
}
