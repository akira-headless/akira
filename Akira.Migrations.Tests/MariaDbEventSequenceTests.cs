using Akira.Testing;
using Xunit;

namespace Akira.Migrations.Tests;

[Trait("Category", "Integration")]
public class MariaDbEventSequenceTests(MariaDbContainerFixture mariaDb) : EventSequenceTests
{
    protected override async Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        await mariaDb.CreateDatabaseAsync(cancellationToken);
}
