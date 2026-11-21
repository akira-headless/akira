using Akira.Server.Testing;
using Akira.Testing;
using Xunit;

namespace Akira.Server.Tests.Endpoints;

// The library queries are translated for each engine on its own: grouping, counting and searching
[Trait("Category", "Integration")]
public class MariaDbItemEndpointsTests(MariaDbContainerFixture mariaDb) : ItemEndpointsTests
{
    protected override async Task<(AkiraApiFactory Factory, IAsyncDisposable? Database)> CreateFactoryAsync(
        CancellationToken cancellationToken)
    {
        var database = await mariaDb.CreateDatabaseAsync(cancellationToken);
        return (new AkiraApiFactory(database.Options) { Instances = LibrarySeed.Instances }, database);
    }
}
