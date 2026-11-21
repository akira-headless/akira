using Akira.Testing;

namespace Akira.Infrastructure.Tests.Sync;

public class SqliteSeriesSyncTests : SeriesSyncTests
{
    protected override Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<TestDatabase>(new SqliteTestDatabase());
}
