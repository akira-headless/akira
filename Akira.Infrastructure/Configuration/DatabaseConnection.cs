using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Akira.Infrastructure.Configuration;

public sealed class DatabaseConnection
{
    private readonly Lazy<ServerVersion>? _serverVersion;

    private DatabaseConnection(DatabaseProvider provider, string connectionString, string displayName, string? sqlitePath)
    {
        Provider = provider;
        ConnectionString = connectionString;
        DisplayName = displayName;
        SqlitePath = sqlitePath;

        if (provider == DatabaseProvider.MariaDb)
        {
            // AutoDetect opens a connection, so it runs once instead of for every DbContext.
            // PublicationOnly keeps a failed attempt from being cached while the server is down.
            _serverVersion = new Lazy<ServerVersion>(
                () => ServerVersion.AutoDetect(connectionString),
                LazyThreadSafetyMode.PublicationOnly);
        }
    }

    public DatabaseProvider Provider { get; }
    public string ConnectionString { get; }
    public string DisplayName { get; }
    public string? SqlitePath { get; }

    public ServerVersion ServerVersion => _serverVersion?.Value
        ?? throw new InvalidOperationException($"{Provider} connections have no server version.");

    public static DatabaseConnection Create(DatabaseOptions options, string contentRootPath)
    {
        return options.Provider switch
        {
            DatabaseProvider.Sqlite => CreateSqlite(options, contentRootPath),
            DatabaseProvider.MariaDb => CreateMariaDb(options),
            _ => throw new InvalidOperationException($"Database provider '{options.Provider}' is not supported.")
        };
    }

    private static DatabaseConnection CreateSqlite(DatabaseOptions options, string contentRootPath)
    {
        var path = Path.GetFullPath(options.SqlitePath, contentRootPath);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true
        }.ToString();

        return new DatabaseConnection(DatabaseProvider.Sqlite, connectionString, path, path);
    }

    private static DatabaseConnection CreateMariaDb(DatabaseOptions options)
    {
        var connectionString = new MySqlConnectionStringBuilder
        {
            Server = options.Host,
            Port = (uint)options.Port,
            Database = options.Name,
            UserID = options.Username,
            Password = options.Password
        }.ConnectionString;

        return new DatabaseConnection(DatabaseProvider.MariaDb, connectionString,
            $"{options.Host}:{options.Port}/{options.Name}", null);
    }
}
