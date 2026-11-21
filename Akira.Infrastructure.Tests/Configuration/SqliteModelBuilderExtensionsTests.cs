using Akira.Testing;

namespace Akira.Infrastructure.Tests.Configuration;

public class SqliteModelBuilderExtensionsTests : ModelBuilderExtensionsTests
{
    protected override Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<TestDatabase>(new SqliteTestDatabase());
}
