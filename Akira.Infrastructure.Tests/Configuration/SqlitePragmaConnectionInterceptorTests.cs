using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Akira.Infrastructure.Configuration;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Configuration;

public class SqlitePragmaConnectionInterceptorTests
{
    [Fact]
    public async Task OpenedConnection_HasPragmasApplied()
    {
        await using var database = new SqliteTestDatabase();
        await using var context = database.CreateAkiraContext();
        var cancellationToken = TestContext.Current.CancellationToken;

        await context.Database.OpenConnectionAsync(cancellationToken);

        Assert.Equal("wal", await ReadPragma(context, "journal_mode", cancellationToken));
        Assert.Equal(SqlitePragmaConnectionInterceptor.BusyTimeoutMilliseconds.ToString(),
            await ReadPragma(context, "busy_timeout", cancellationToken));
        Assert.Equal("1", await ReadPragma(context, "foreign_keys", cancellationToken));
    }

    [Fact]
    public async Task CanConnect_ToExistingDatabase_ReturnsTrue()
    {
        await using var database = new SqliteTestDatabase();
        var cancellationToken = TestContext.Current.CancellationToken;
        // Created outside EF Core like the migration runner does, so the file is still in the
        // default rollback journal mode. EF Core checks existence over a separate read-only
        // connection, which the interceptor also sees, and switching to WAL there fails.
        await using (var setup = new SqliteConnection(database.Connection.ConnectionString))
        {
            await setup.OpenAsync(cancellationToken);
        }

        await using var context = database.CreateAkiraContext();

        Assert.True(await context.Database.CanConnectAsync(cancellationToken));
    }

    [Fact]
    public async Task CanConnect_ToMissingDatabase_ReturnsFalse()
    {
        await using var database = new SqliteTestDatabase();
        await using var context = database.CreateAkiraContext();

        Assert.False(await context.Database.CanConnectAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<string?> ReadPragma(DbContext context, string pragma, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA {pragma}";
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
    }
}
