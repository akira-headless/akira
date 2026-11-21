using Akira.Testing;
using Xunit;

namespace Akira.Migrations.Tests;

[Trait("Category", "Integration")]
public class MariaDbMigrationDatabaseTests(MariaDbContainerFixture mariaDb) : MigrationDatabaseTests
{
    protected override string Token => MigrationDatabase.MariaDb;

    protected override async Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        await mariaDb.CreateDatabaseAsync(cancellationToken);
}
