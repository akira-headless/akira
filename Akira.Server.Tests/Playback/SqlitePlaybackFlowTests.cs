using Akira.Infrastructure.Configuration;

namespace Akira.Server.Tests.Playback;

public class SqlitePlaybackFlowTests : PlaybackFlowTests
{
    protected override Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken) =>
        Task.FromResult<(DatabaseOptions?, IAsyncDisposable?)>((null, null));
}
