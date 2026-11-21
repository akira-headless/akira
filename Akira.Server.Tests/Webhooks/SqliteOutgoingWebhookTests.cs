using Akira.Infrastructure.Configuration;

namespace Akira.Server.Tests.Webhooks;

public class SqliteOutgoingWebhookTests : OutgoingWebhookTests
{
    protected override Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(DatabaseOptions?, IAsyncDisposable?)>((null, null));
}
