namespace Akira.Core.Library;

/// <summary>
/// Translates a path from one system's view of the media share into another's.
/// </summary>
public sealed class PathMapper
{
    private readonly IReadOnlyList<PathMapping> _mappings;

    public PathMapper(IEnumerable<PathMapping> mappings)
    {
        // Longest prefix first, so /data/media/anime wins over /data/media
        _mappings = mappings
            .Select(mapping => new PathMapping(TrimEnd(mapping.From), TrimEnd(mapping.To)))
            .OrderByDescending(mapping => mapping.From.Length)
            .ToList();
    }

    public static PathMapper None { get; } = new([]);

    /// <summary>
    /// Maps a path through the first matching prefix, or returns it unchanged when none matches.
    /// </summary>
    public string Map(string path) => TryMap(path, out var mapped) ? mapped : path;

    /// <summary>
    /// Maps a path through the first matching prefix, and says whether one matched.
    /// </summary>
    public bool TryMap(string path, out string mapped)
    {
        foreach (var mapping in _mappings)
        {
            var comparison = IsWindowsPath(mapping.From) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(mapping.From, comparison))
            {
                continue;
            }

            var remainder = path[mapping.From.Length..];
            if (remainder.Length > 0 && remainder[0] is not ('/' or '\\'))
            {
                // /movies must not match /movies-4k
                continue;
            }

            var separator = mapping.To.Contains('\\') && !mapping.To.Contains('/') ? '\\' : '/';
            mapped = mapping.To + remainder.Replace('/', separator).Replace('\\', separator);
            return true;
        }

        mapped = path;
        return false;
    }

    private static bool IsWindowsPath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || (path.Length >= 2 && path[1] == ':');

    // The root becomes an empty prefix, so mapping to or from / joins without a doubled separator
    private static string TrimEnd(string path) => path.TrimEnd('/', '\\');
}
