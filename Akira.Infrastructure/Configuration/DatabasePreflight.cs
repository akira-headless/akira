using MySqlConnector;

namespace Akira.Infrastructure.Configuration;

public static class DatabasePreflight
{
    public const string RequiredMariaDbCharacterSet = "utf8mb4";

    /// <summary>
    /// Checks that the database can hold the schema before any migration runs.
    /// </summary>
    public static async Task RunAsync(DatabaseConnection connection, CancellationToken cancellationToken)
    {
        switch (connection.Provider)
        {
            case DatabaseProvider.Sqlite:
                // SQLite creates a missing database file but not a missing directory
                var directory = Path.GetDirectoryName(connection.SqlitePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                break;
            case DatabaseProvider.MariaDb:
                await EnsureMariaDbCharacterSetAsync(connection, cancellationToken);
                _ = connection.ServerVersion;
                break;
        }
    }

    // New tables take the database default character set. On latin1 or utf8mb3, which older
    // MariaDB installs still default to, Japanese titles and emoji fail to insert or become "?".
    private static async Task EnsureMariaDbCharacterSetAsync(DatabaseConnection connection, CancellationToken cancellationToken)
    {
        await using var mariaDb = new MySqlConnection(connection.ConnectionString);
        await mariaDb.OpenAsync(cancellationToken);
        await using var command = mariaDb.CreateCommand();
        command.CommandText = "SELECT @@character_set_database";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var characterSet = Convert.ToString(result);

        if (!string.Equals(characterSet, RequiredMariaDbCharacterSet, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"MariaDB database '{mariaDb.Database}' uses character set '{characterSet}', Akira needs " +
                $"{RequiredMariaDbCharacterSet}. Before the first start run: ALTER DATABASE `{mariaDb.Database}` " +
                $"CHARACTER SET {RequiredMariaDbCharacterSet} COLLATE {RequiredMariaDbCharacterSet}_unicode_ci;");
        }
    }
}
