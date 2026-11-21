using Microsoft.EntityFrameworkCore;
using Akira.Testing;
using Xunit;

namespace Akira.Migrations.Tests;

[Trait("Category", "Integration")]
public class MariaDbSchemaTests(MariaDbContainerFixture mariaDb) : SchemaTests
{
    protected override async Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        await mariaDb.CreateDatabaseAsync(cancellationToken);

    // SQLite accepts any value in any column, so types only mean something on MariaDB. There a
    // TEXT column under an EF longtext, or DATETIME under datetime(6), truncates what SQLite keeps.
    [Fact]
    public async Task MigratedSchema_ColumnTypesMatchModel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        await using var context = database.CreateAkiraContext();

        Assert.Empty(await SchemaComparer.FindTypeMismatchesAsync(context, database, cancellationToken));
    }

    [Fact]
    public async Task FindTypeMismatches_ReportsNarrowerColumns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        await ExecuteAsync(database, "CREATE TABLE probe_rows (id INT, title TEXT, added_at DATETIME)", cancellationToken);
        await using var context = database.CreateContext<ProbeContext>(options => new ProbeContext(options));

        var problems = await SchemaComparer.FindTypeMismatchesAsync(context, database, cancellationToken);

        Assert.Equal(
            [
                "Column probe_rows.added_at is datetime in the database but datetime(6) in the EF model.",
                "Column probe_rows.title is text in the database but longtext in the EF model."
            ],
            problems.Order());
    }

    // FluentMigrator turns AsString(n) into NVARCHAR(n), which MariaDB stores as utf8mb3. That
    // cannot hold emoji or some CJK characters, the database default of utf8mb4 can.
    [Fact]
    public async Task MigratedSchema_TextColumnsAreUtf8Mb4()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);

        // FluentMigrator creates version_info itself with AsString, so its description column is
        // utf8mb3. It only ever holds migration class names.
        Assert.Empty(await SchemaComparer.FindNonUtf8Mb4ColumnsAsync(database, [VersionTable], cancellationToken));
    }

    [Fact]
    public async Task FindNonUtf8Mb4Columns_ReportsNVarchar()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateDatabaseAsync(cancellationToken);
        await ExecuteAsync(database, "CREATE TABLE probe_rows (title NVARCHAR(50), overview TEXT)", cancellationToken);

        var problems = await SchemaComparer.FindNonUtf8Mb4ColumnsAsync(database, [], cancellationToken);

        Assert.Equal(["Column probe_rows.title is utf8mb3, not utf8mb4."], problems);
    }
}
