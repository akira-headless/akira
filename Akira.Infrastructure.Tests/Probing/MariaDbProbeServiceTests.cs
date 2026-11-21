using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Probing;

[Trait("Category", "Integration")]
public class MariaDbProbeServiceTests(MariaDbContainerFixture mariaDb) : ProbeServiceTests
{
    protected override async Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        await mariaDb.CreateDatabaseAsync(cancellationToken);
}
