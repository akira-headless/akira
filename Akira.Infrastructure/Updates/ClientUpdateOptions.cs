namespace Akira.Infrastructure.Updates;

public sealed class ClientUpdateOptions
{
    public const string SectionName = "ClientUpdates";

    // Relative to the working directory like the SQLite database, so it lives in the config volume
    public string Path { get; set; } = "config/client-updates";

    // Keyed by the platform the apps ask for, such as android or linux; none configured means no updates
    public Dictionary<string, ClientUpdateSource> Sources { get; set; } = [];

    public string ResolvePath() => System.IO.Path.GetFullPath(Path);
}
