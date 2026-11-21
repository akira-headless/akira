using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Interfaces.Sync;

namespace Akira.Infrastructure.Sync;

public class LibrarySyncService : ILibrarySyncService
{
    private readonly AkiraDbContext _dbContext;
    private readonly IMovieSync _movieSync;
    private readonly ISeriesSync _seriesSync;
    private readonly SyncLock _syncLock;
    private readonly IOptions<ArrOptions> _options;
    private readonly ILogger<LibrarySyncService> _logger;

    public LibrarySyncService(
        AkiraDbContext dbContext,
        IMovieSync movieSync,
        ISeriesSync seriesSync,
        SyncLock syncLock,
        IOptions<ArrOptions> options,
        ILogger<LibrarySyncService> logger)
    {
        _dbContext = dbContext;
        _movieSync = movieSync;
        _seriesSync = seriesSync;
        _syncLock = syncLock;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LibrarySyncReport> SyncAsync(CancellationToken cancellationToken)
    {
        var instances = _options.Value.Instances;
        if (instances.Count == 0)
        {
            // Far more likely a lost environment variable than a wish to empty the library
            _logger.LogWarning("No Radarr or Sonarr instances are configured, the library is left as it is");
            return new LibrarySyncReport([], []);
        }

        // One instance failing does not stop the others, each reports on its own. The gate is theirs to take, around
        // what they write and never around a call to Radarr or Sonarr.
        var results = new List<SyncResult>();
        foreach (var instance in instances)
        {
            SyncResult result;
            try
            {
                result = instance.Kind == ArrKind.Radarr
                    ? await _movieSync.SyncAsync(instance, cancellationToken)
                    : await _seriesSync.SyncAsync(instance, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // An answer the sync cannot work with, or a row the database refuses. What it tracked so far
                // is dropped, or the next instance would save half of it.
                _logger.LogError(exception, "{Instance} sync stopped on an error nobody expected", instance.DisplayName);
                _dbContext.ChangeTracker.Clear();
                result = SyncResult.Failed(instance.Key, exception.Message);
            }

            results.Add(result);
            Log(instance, result);
        }

        var removed = await RemoveUnlinkedAsync(instances, cancellationToken);
        return new LibrarySyncReport(results, removed);
    }

    /// <inheritdoc />
    public async Task<LibrarySyncReport> RefreshAsync(string instanceKey, int arrId, CancellationToken cancellationToken)
    {
        var instances = _options.Value.Instances;
        var instance = instances.FirstOrDefault(instance => instance.Key == instanceKey);
        if (instance is null)
        {
            return new LibrarySyncReport([], []);
        }

        var result = instance.Kind == ArrKind.Radarr
            ? await _movieSync.SyncMovieAsync(instance, arrId, cancellationToken)
            : await _seriesSync.SyncSeriesAsync(instance, arrId, cancellationToken);
        if (result.Status != SyncStatus.Succeeded)
        {
            Log(instance, result);
        }

        var removed = await RemoveUnlinkedAsync(instances, cancellationToken);
        return new LibrarySyncReport([result], removed);
    }

    private async Task<IReadOnlyList<ItemChange>> RemoveUnlinkedAsync(List<ArrInstanceOptions> instances, CancellationToken cancellationToken)
    {
        await _syncLock.Gate.WaitAsync(cancellationToken);
        try
        {
            return await RemoveUnlinkedInGateAsync(instances, cancellationToken);
        }
        finally
        {
            _syncLock.Gate.Release();
        }
    }

    private async Task<IReadOnlyList<ItemChange>> RemoveUnlinkedInGateAsync(List<ArrInstanceOptions> instances, CancellationToken cancellationToken)
    {
        var configuredKeys = instances.Select(instance => instance.Key).ToList();

        // An instance taken out of the configuration no longer holds anything
        await _dbContext.ArrLinks.Where(link => !configuredKeys.Contains(link.InstanceKey)).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.MediaFiles.Where(file => !configuredKeys.Contains(file.InstanceKey)).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.MediaImages.Where(image => !configuredKeys.Contains(image.InstanceKey)).ExecuteDeleteAsync(cancellationToken);

        // A failed instance still holds its links, so only items that are really gone everywhere
        // match. Seasons and episodes of a removed series match too, and cascade with it anyway.
        var unlinked = _dbContext.MediaItems.Where(item => !item.Links.Any());
        var removed = await unlinked.Select(ItemChange.FromItem).ToListAsync(cancellationToken);

        if (removed.Count > 0)
        {
            await unlinked.ExecuteDeleteAsync(cancellationToken);
            _logger.LogInformation("Removed {Count} items that no instance holds any more", removed.Count);
        }

        return removed;
    }

    private void Log(ArrInstanceOptions instance, SyncResult result)
    {
        var level = result.Status switch
        {
            SyncStatus.Failed => LogLevel.Error,
            SyncStatus.PartiallySucceeded => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(level, "{Instance} sync {Status}: {Added} added, {Updated} updated, {Unlinked} unlinked",
            instance.DisplayName, result.Status, result.Added, result.Updated, result.Unlinked);

        foreach (var problem in result.Problems)
        {
            _logger.Log(level, "{Instance} sync: {Problem}", instance.DisplayName, problem);
        }
    }
}
