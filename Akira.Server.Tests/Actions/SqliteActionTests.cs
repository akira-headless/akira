using Akira.Infrastructure.Configuration;

namespace Akira.Server.Tests.Actions;

public class SqliteActionTests : ActionTests
{
    protected override Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(DatabaseOptions?, IAsyncDisposable?)>((null, null));
}
