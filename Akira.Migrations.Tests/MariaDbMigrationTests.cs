using Akira.Testing;
using Xunit;

namespace Akira.Migrations.Tests;

[Trait("Category", "Integration")]
public class MariaDbMigrationTests(MariaDbContainerFixture mariaDb) : MigrationTests
{
    protected override async Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        await mariaDb.CreateDatabaseAsync(cancellationToken);

    // MariaDB adds foreign keys after their table, in statements of their own, so only here can a table lose one
    [Fact]
    public async Task MigrateUp_AfterARunThatStoppedBeforeAForeignKey_AddsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        using var harness = new MigrationHarness(database);
        harness.MigrateUp();

        await ExecuteAsync(database, "ALTER TABLE arr_links DROP FOREIGN KEY fk_arr_links_media_item_id", cancellationToken);
        await ExecuteAsync(database, "DELETE FROM version_info WHERE version > 1", cancellationToken);

        harness.MigrateUp();

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM information_schema.referential_constraints
            WHERE constraint_schema = DATABASE() AND constraint_name = 'fk_arr_links_media_item_id'
                AND table_name = 'arr_links' AND referenced_table_name = 'media_items' AND delete_rule = 'CASCADE'
            """;
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)));
    }
}
