namespace Akira.Infrastructure.Sync;

/// <summary>
/// Holds the server's own folders (its program, settings, database, key ring, trickplay and fingerprints), which no file
/// from Radarr or Sonarr may point into.
/// </summary>
public sealed class ServerFolders
{
    public static ServerFolders None { get; } = new([]);

    public IReadOnlyList<string> Folders { get; }

    public ServerFolders(IEnumerable<string> folders)
    {
        Folders = folders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(Path.GetFullPath).Distinct().ToList();
    }
}
