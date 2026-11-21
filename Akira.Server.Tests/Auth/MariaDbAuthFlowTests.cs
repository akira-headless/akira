using Akira.Infrastructure.Configuration;
using Akira.Testing;
using Xunit;

namespace Akira.Server.Tests.Auth;

[Trait("Category", "Integration")]
public class MariaDbAuthFlowTests(MariaDbContainerFixture mariaDb) : AuthFlowTests
{
    protected override async Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = await mariaDb.CreateDatabaseAsync(cancellationToken);
        return (database.Options, database);
    }
}
