using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Sync;

[Trait("Category", "Integration")]
public class MariaDbSeriesSyncTests(MariaDbContainerFixture mariaDb) : SeriesSyncTests
{
    protected override async Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        await mariaDb.CreateDatabaseAsync(cancellationToken);
}
