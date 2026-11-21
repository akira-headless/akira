using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Configuration;

[Trait("Category", "Integration")]
public class MariaDbModelBuilderExtensionsTests(MariaDbContainerFixture mariaDb) : ModelBuilderExtensionsTests
{
    protected override async Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        await mariaDb.CreateDatabaseAsync(cancellationToken);
}
