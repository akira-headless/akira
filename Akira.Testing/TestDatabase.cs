using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Akira.Infrastructure.Configuration;
using Akira.Infrastructure.Data;
using Akira.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Akira.Testing;

public abstract class TestDatabase : IAsyncDisposable
{
    protected TestDatabase(DatabaseConnection connection)
    {
        Connection = connection;
    }

    public DatabaseConnection Connection { get; }

    public TContext CreateContext<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        DatabaseConfiguration.ConfigureDbContext(builder, Connection);
        return factory(builder.Options);
    }

    public AkiraDbContext CreateAkiraContext() => CreateContext<AkiraDbContext>(options => new AkiraDbContext(options));

    public void Migrate()
    {
        using var services = new ServiceCollection()
            .AddFluentMigrator(_ => Connection)
            .BuildServiceProvider();
        MigrationConfiguration.RunMigrations(services);
    }

    public abstract Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists the columns of a table in their declared order, or none when the table does not exist.
    /// </summary>
    public abstract Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string table, CancellationToken cancellationToken);

    public abstract ValueTask DisposeAsync();

    protected static async Task<IReadOnlyList<ColumnInfo>> ReadColumnsAsync(
        DbConnection connection,
        string sql,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return columns;
    }
}
