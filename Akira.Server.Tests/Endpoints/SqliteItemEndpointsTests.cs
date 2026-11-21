using Akira.Server.Testing;

namespace Akira.Server.Tests.Endpoints;

public class SqliteItemEndpointsTests : ItemEndpointsTests
{
    protected override Task<(AkiraApiFactory Factory, IAsyncDisposable? Database)> CreateFactoryAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<(AkiraApiFactory, IAsyncDisposable?)>((new AkiraApiFactory { Instances = LibrarySeed.Instances }, null));
}
