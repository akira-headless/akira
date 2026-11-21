using Akira.Testing;

namespace Akira.Migrations.Tests;

public class SqliteEventSequenceTests : EventSequenceTests
{
    protected override Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<TestDatabase>(new SqliteTestDatabase());
}
