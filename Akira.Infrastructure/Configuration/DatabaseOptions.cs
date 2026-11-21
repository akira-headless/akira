namespace Akira.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const int DefaultMariaDbPort = 3306;

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;
    public string SqlitePath { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = DefaultMariaDbPort;
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
