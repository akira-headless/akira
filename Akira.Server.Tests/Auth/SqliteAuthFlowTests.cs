using Akira.Infrastructure.Configuration;

namespace Akira.Server.Tests.Auth;

public class SqliteAuthFlowTests : AuthFlowTests
{
    protected override Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(DatabaseOptions?, IAsyncDisposable?)>((null, null));
}
