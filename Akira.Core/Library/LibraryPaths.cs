namespace Akira.Core.Library;

/// <summary>
/// Turns a file path from Radarr or Sonarr into one on this server, or refuses it. A path an upstream made up must not
/// reach the server's own files: it falls under a mapping when there are any, never climbs with "..", and never lies in a protected folder.
/// </summary>
public sealed class LibraryPaths
{
    // Where no library lives on Linux, and what would hand out the machine's secrets
    public static IReadOnlyList<string> SystemFolders { get; } = ["/proc", "/sys", "/dev", "/etc", "/run", "/var/run"];

    private readonly PathMapper _mapper;
    private readonly bool _requireMapping;
    private readonly IReadOnlyList<string> _protectedFolders;

    public LibraryPaths(
        PathMapper mapper,
        bool requireMapping,
        IReadOnlyList<string> protectedFolders)
    {
        _mapper = mapper;
        _requireMapping = requireMapping;
        _protectedFolders = protectedFolders;
    }

    public string? Resolve(string arrPath)
    {
        if (!_mapper.TryMap(arrPath, out var path) && _requireMapping)
        {
            return null;
        }

        // An empty part too: Linux opens //etc/shadow as /etc/shadow, which the folders below would not see
        if (RootLength(path) is not { } root || path[root..].Split('/', '\\').Any(part => part is "" or "." or ".."))
        {
            return null;
        }

        var isProtected = _protectedFolders.Concat(SystemFolders).Any(folder => IsInside(path, folder));
        return isProtected ? null : path;
    }

    private static int? RootLength(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return 2;
        }

        if (path.StartsWith('/'))
        {
            return 1;
        }

        var hasDrive = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        return hasDrive ? 3 : null;
    }

    private static bool IsInside(string path, string folder)
    {
        var root = folder.TrimEnd('/', '\\');
        if (root.Length == 0)
        {
            return false;
        }

        var windows = (root.Length >= 2 && root[1] == ':') || root.StartsWith(@"\\", StringComparison.Ordinal);
        if (windows)
        {
            (path, root) = (path.Replace('/', '\\'), root.Replace('/', '\\'));
        }

        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(root, comparison)
               && (path.Length == root.Length || path[root.Length] is '/' or '\\');
    }
}
