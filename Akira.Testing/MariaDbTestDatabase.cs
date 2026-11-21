using System.Data.Common;
using MySqlConnector;
using Akira.Infrastructure.Configuration;

namespace Akira.Testing;

public sealed class MariaDbTestDatabase : TestDatabase
{
    private readonly string _adminConnectionString;
    private readonly string _name;

    internal MariaDbTestDatabase(DatabaseOptions options, string adminConnectionString)
        : base(DatabaseConnection.Create(options, Path.GetTempPath()))
    {
        _adminConnectionString = adminConnectionString;
        _name = options.Name;
        Options = options;
    }

    public DatabaseOptions Options { get; }

    public override async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(Connection.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public override async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string table, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await ReadColumnsAsync(connection,
            """
            SELECT column_name, column_type, character_set_name
            FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = @table
            ORDER BY ordinal_position
            """,
            table, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        MySqlConnection.ClearAllPools();

        await using var connection = new MySqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{_name}`";
        await command.ExecuteNonQueryAsync();
    }
}
