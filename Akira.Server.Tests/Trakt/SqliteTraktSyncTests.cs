using Akira.Infrastructure.Configuration;

namespace Akira.Server.Tests.Trakt;

public class SqliteTraktSyncTests : TraktSyncTests
{
    protected override Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(DatabaseOptions?, IAsyncDisposable?)>((null, null));
}
