using Microsoft.EntityFrameworkCore;
using Akira.Testing;
using Xunit;

namespace Akira.Migrations.Tests;

// The EF Core model and the FluentMigrator migrations describe the same schema twice and nothing
// else checks that they agree. A mismatch only fails at runtime, on the first query that touches it.
public abstract class SchemaTests
{
    protected const string VersionTable = "version_info";

    protected abstract Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task MigratedSchema_ContainsEveryMappedTableAndColumn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        await using var context = database.CreateAkiraContext();

        Assert.Empty(await SchemaComparer.FindMissingAsync(context, database, cancellationToken));
    }

    [Fact]
    public async Task FindMissing_ReportsAbsentTableAndColumn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        await ExecuteAsync(database, "CREATE TABLE probe_rows (id INT, title TEXT)", cancellationToken);
        await using var context = database.CreateContext<ProbeContext>(options => new ProbeContext(options));

        var problems = await SchemaComparer.FindMissingAsync(context, database, cancellationToken);

        Assert.Equal(
            ["Column probe_rows.added_at is mapped but does not exist.", "Table other_rows is mapped but does not exist."],
            problems.Order());
    }

    protected async Task<TestDatabase> CreateMigratedDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = await CreateDatabaseAsync(cancellationToken);
        using var harness = new MigrationHarness(database);
        harness.MigrateUp();
        return database;
    }

    protected static async Task ExecuteAsync(TestDatabase database, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    protected sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> ProbeRows => Set<ProbeRow>();
        public DbSet<OtherRow> OtherRows => Set<OtherRow>();
    }

    protected sealed class ProbeRow
    {
        public int Id { get; set; }
        public required string Title { get; set; }
        public DateTime AddedAt { get; set; }
    }

    protected sealed class OtherRow
    {
        public int Id { get; set; }
    }
}
