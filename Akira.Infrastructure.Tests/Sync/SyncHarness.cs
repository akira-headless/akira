using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Sync;
using Akira.Testing;

namespace Akira.Infrastructure.Tests.Sync;

/// <summary>
/// Runs a full library sync the way the worker does, against a test database and fake clients.
/// </summary>
public static class SyncHarness
{
    public static async Task<LibrarySyncReport> SyncAsync(
        TestDatabase database,
        FakeArrClientFactory clients,
        IEnumerable<ArrInstanceOptions> instances,
        CancellationToken cancellationToken,
        SyncLock? syncLock = null)
    {
        await using var context = database.CreateAkiraContext();
        return await Create(context, clients, instances, syncLock).SyncAsync(cancellationToken);
    }

    /// <summary>
    /// Runs the refresh of one movie or series that a webhook triggers.
    /// </summary>
    public static async Task<LibrarySyncReport> RefreshAsync(
        TestDatabase database,
        FakeArrClientFactory clients,
        IEnumerable<ArrInstanceOptions> instances,
        string instanceKey,
        int arrId,
        CancellationToken cancellationToken,
        SyncLock? syncLock = null)
    {
        await using var context = database.CreateAkiraContext();
        return await Create(context, clients, instances, syncLock).RefreshAsync(instanceKey, arrId, cancellationToken);
    }

    // A sync and a refresh that run at the same time share the lock, as they do in the server
    private static LibrarySyncService Create(
        AkiraDbContext context,
        FakeArrClientFactory clients,
        IEnumerable<ArrInstanceOptions> instances,
        SyncLock? shared)
    {
        var syncLock = shared ?? new SyncLock();
        var options = Options.Create(new ArrOptions { Instances = instances.ToList() });
        return new LibrarySyncService(context,
            new MovieSync(context, clients, ServerFolders.None, syncLock, options, NullLogger<MovieSync>.Instance),
            new SeriesSync(context, clients, ServerFolders.None, syncLock, options, NullLogger<SeriesSync>.Instance),
            syncLock,
            options,
            NullLogger<LibrarySyncService>.Instance);
    }
}
