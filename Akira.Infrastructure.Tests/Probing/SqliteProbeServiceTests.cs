using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Probing;

[Trait("Category", "Integration")]
public class SqliteProbeServiceTests : ProbeServiceTests
{
    protected override Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<TestDatabase>(new SqliteTestDatabase());
}
