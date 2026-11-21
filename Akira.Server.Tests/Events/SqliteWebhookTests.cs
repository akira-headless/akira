using Akira.Infrastructure.Configuration;

namespace Akira.Server.Tests.Events;

public class SqliteWebhookTests : WebhookTests
{
    protected override Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(DatabaseOptions?, IAsyncDisposable?)>((null, null));
}
