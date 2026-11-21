using Akira.Testing;
using Xunit;

namespace Akira.Migrations.Tests;

public abstract class MigrationTests
{
    protected abstract Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task MigrateUp_AppliesEveryMigration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        using var harness = new MigrationHarness(database);

        harness.MigrateUp();

        Assert.Equal(MigrationHarness.MigrationTypes.Count, (await harness.GetAppliedVersionsAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task MigrateUp_IsSafeToRunTwice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        using var harness = new MigrationHarness(database);

        harness.MigrateUp();
        harness.MigrateUp();

        Assert.Equal(MigrationHarness.MigrationTypes.Count, (await harness.GetAppliedVersionsAsync(cancellationToken)).Count);
    }

    // MariaDB commits every DDL statement, so a migration that failed halfway runs again over part of its own work
    [Fact]
    public async Task MigrateUp_AfterAHalfFinishedRun_FinishesTheWork()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        using var harness = new MigrationHarness(database);
        harness.MigrateUp();

        await ExecuteAsync(database,
            """
            INSERT INTO media_items (id, type, identity_key, title, sort_title, created_at, updated_at)
            VALUES ('0190a000-0000-7000-8000-000000000001', 'Movie', 'movie:tmdb:1', 'Amélie', 'amelie',
                    '2026-01-01 00:00:00', '2026-01-01 00:00:00')
            """, cancellationToken);
        await ExecuteAsync(database, "UPDATE media_items SET search_title = NULL", cancellationToken);
        await ExecuteAsync(database, "DROP TABLE marker_scans", cancellationToken);
        await ExecuteAsync(database, "ALTER TABLE media_items DROP COLUMN certification", cancellationToken);
        await ExecuteAsync(database, "DELETE FROM version_info WHERE version > 1", cancellationToken);

        harness.MigrateUp();

        Assert.Equal(MigrationHarness.MigrationTypes.Count, (await harness.GetAppliedVersionsAsync(cancellationToken)).Count);
        await using (var context = database.CreateAkiraContext())
        {
            Assert.Empty(await SchemaComparer.FindMissingAsync(context, database, cancellationToken));
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT search_title FROM media_items WHERE identity_key = 'movie:tmdb:1'";
        Assert.Equal("amelie", await command.ExecuteScalarAsync(cancellationToken));
    }

    [Fact]
    public async Task MigrateDown_ToZero_ThenUpAgain_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        using var harness = new MigrationHarness(database);

        harness.MigrateUp();
        harness.MigrateDown(0);
        Assert.Empty(await harness.GetAppliedVersionsAsync(cancellationToken));

        harness.MigrateUp();
        Assert.Equal(MigrationHarness.MigrationTypes.Count, (await harness.GetAppliedVersionsAsync(cancellationToken)).Count);
    }

    protected static async Task ExecuteAsync(TestDatabase database, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
