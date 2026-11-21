using System.Data.Common;
using Microsoft.Data.Sqlite;
using Akira.Infrastructure.Configuration;

namespace Akira.Testing;

public sealed class SqliteTestDatabase()
    : TestDatabase(DatabaseConnection.Create(
        new DatabaseOptions
        {
            Provider = DatabaseProvider.Sqlite,
            SqlitePath = $"akira_test_{Guid.NewGuid():N}.db"
        },
        Path.GetTempPath()))
{
    public override async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(Connection.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public override async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string table, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await ReadColumnsAsync(connection,
            "SELECT name, type, NULL FROM pragma_table_info(@table) ORDER BY cid",
            table, cancellationToken);
    }

    public override ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(Connection.SqlitePath + suffix);
        }

        return ValueTask.CompletedTask;
    }
}
