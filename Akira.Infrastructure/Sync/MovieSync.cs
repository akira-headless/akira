using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Library;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Radarr;
using Akira.Infrastructure.Interfaces.Integrations;
using Akira.Infrastructure.Interfaces.Sync;

namespace Akira.Infrastructure.Sync;

public class MovieSync : IMovieSync
{
    // Loaded, saved and let go this many at a time, so a large library is never tracked as a whole
    private const int BatchSize = 200;

    // After this many movies the database refused one by one, the rest would go the same way
    private const int MaxSkipped = 20;

    private readonly AkiraDbContext _dbContext;
    private readonly IArrClientFactory _clients;
    private readonly ServerFolders _folders;
    private readonly SyncLock _syncLock;
    private readonly IOptions<ArrOptions> _options;
    private readonly ILogger<MovieSync> _logger;

    public MovieSync(
        AkiraDbContext dbContext,
        IArrClientFactory clients,
        ServerFolders folders,
        SyncLock syncLock,
        IOptions<ArrOptions> options,
        ILogger<MovieSync> logger)
    {
        _dbContext = dbContext;
        _clients = clients;
        _folders = folders;
        _syncLock = syncLock;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncAsync(ArrInstanceOptions instance, CancellationToken cancellationToken)
    {
        var radarr = _clients.CreateRadarr(instance);
        IReadOnlyList<RadarrMovie> movies;
        try
        {
            var version = _syncLock.Version;
            movies = await radarr.GetMoviesAsync(cancellationToken);
            movies = await _syncLock.EnterAsync(version, movies, () => radarr.GetMoviesAsync(cancellationToken), cancellationToken);
        }
        catch (UpstreamException exception)
        {
            // Nothing is unlinked when the list could not be read
            return SyncResult.Failed(instance.Key, exception.Message);
        }

        var run = new MovieRun(instance, instance.CreateLibraryPaths(_folders.Folders), new SyncChanges(instance.Key));
        try
        {
            foreach (var batch in movies.Chunk(BatchSize))
            {
                await SaveBatchAsync(run, Accept(run, batch), cancellationToken);
            }

            await UnlinkMissingAsync(run, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // What the batches before it saved stays, and is reported. Nothing is unlinked after this.
            _logger.LogError(exception, "{Instance} sync stopped on an error nobody expected", instance.DisplayName);
            run.Changes.Problem($"The {instance.DisplayName} sync stopped halfway: {LibraryItems.Reason(exception)}");
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
            _syncLock.Advance();
            _syncLock.Gate.Release();
        }

        return run.Changes.ToResult();
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncMovieAsync(ArrInstanceOptions instance, int movieId, CancellationToken cancellationToken)
    {
        var radarr = _clients.CreateRadarr(instance);
        RadarrMovie? movie;
        try
        {
            var version = _syncLock.Version;
            movie = await GetOrNullAsync(radarr, movieId, cancellationToken);
            movie = await _syncLock.EnterAsync(version, movie, () => GetOrNullAsync(radarr, movieId, cancellationToken), cancellationToken);
        }
        catch (UpstreamException exception)
        {
            return SyncResult.Failed(instance.Key, exception.Message);
        }

        var run = new MovieRun(instance, instance.CreateLibraryPaths(_folders.Folders), new SyncChanges(instance.Key));
        try
        {
            // A batch of refreshes shares one scope, and what the previous one tracked may be deleted by now
            _dbContext.ChangeTracker.Clear();

            // The movie under its current key, and whatever this instance linked to that Radarr id before.
            // Only those are loaded, so only those can be unlinked.
            var accepted = Accept(run, movie is null ? [] : [movie]);
            var key = accepted.Count > 0 ? accepted[0].Key : null;
            var instanceKey = instance.Key;
            var items = await LoadAsync(item => item.IdentityKey == key
                                                || item.Links.Any(link => link.InstanceKey == instanceKey && link.ArrId == movieId),
                cancellationToken);
            foreach (var (current, currentKey) in accepted)
            {
                Apply(run, current, currentKey, items);
            }

            var unlinked = Unlink(run, items.Values);
            await run.Changes.SaveAsync(_dbContext, unlinked, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "{Instance} movie {MovieId} was skipped on an error nobody expected", instance.DisplayName, movieId);
            run.Changes.Problem($"Kept {instance.DisplayName} movie {movieId} unchanged: {LibraryItems.Reason(exception)}");
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
            _syncLock.Advance();
            _syncLock.Gate.Release();
        }

        return run.Changes.ToResult();
    }

    private static async Task<RadarrMovie?> GetOrNullAsync(IRadarrClient radarr, int movieId, CancellationToken cancellationToken)
    {
        try
        {
            return await radarr.GetMovieAsync(movieId, cancellationToken);
        }
        catch (UpstreamException exception) when (exception.Failure == UpstreamFailure.NotFound)
        {
            // Deleted in Radarr, so whatever this instance linked to it goes
            return null;
        }
    }

    private Task<Dictionary<string, MediaItem>> LoadAsync(Expression<Func<MediaItem, bool>> filter, CancellationToken cancellationToken) =>
        _dbContext.MediaItems
            .Include(item => item.Links)
            .Include(item => item.Files)
            .Include(item => item.Images)
            .Where(item => item.Type == MediaItemType.Movie)
            .Where(filter)
            .ToDictionaryAsync(item => item.IdentityKey, cancellationToken);

    // Seen holds every movie Radarr still has, also one that is then skipped, so a skipped movie is never unlinked
    private static List<(RadarrMovie Movie, string Key)> Accept(MovieRun run, IReadOnlyList<RadarrMovie> movies)
    {
        var instance = run.Instance;
        var accepted = new List<(RadarrMovie Movie, string Key)>();
        foreach (var movie in movies)
        {
            if (movie.Id <= 0 || string.IsNullOrWhiteSpace(movie.Title))
            {
                run.Changes.Problem($"Skipped {instance.DisplayName} movie {movie.Id} without an id or title.");
                continue;
            }

            var key = IdentityKeys.Movie(movie.TmdbId, instance.Key, movie.Id);
            if (!run.Seen.Add(key))
            {
                run.Changes.Problem($"Skipped {instance.DisplayName} movie {movie.Id}, another movie already has {key}.");
                continue;
            }

            accepted.Add((movie, key));
        }

        return accepted;
    }

    private async Task SaveBatchAsync(MovieRun run, IReadOnlyList<(RadarrMovie Movie, string Key)> movies, CancellationToken cancellationToken)
    {
        if (movies.Count == 0)
        {
            return;
        }

        try
        {
            // Every movie with the key, not only this instance's: the same movie in another Radarr is the same item
            var keys = movies.Select(movie => movie.Key).ToList();
            var items = await LoadAsync(item => keys.Contains(item.IdentityKey), cancellationToken);
            foreach (var (movie, key) in movies)
            {
                Apply(run, movie, key, items);
            }

            await run.Changes.SaveAsync(_dbContext, [], cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _dbContext.ChangeTracker.Clear();
            if (movies.Count > 1)
            {
                // One movie the database refuses must not cost the rest of its batch
                foreach (var movie in movies)
                {
                    await SaveBatchAsync(run, [movie], cancellationToken);
                }

                return;
            }

            if (++run.Skipped > MaxSkipped)
            {
                throw;
            }

            var instance = run.Instance;
            _logger.LogError(exception, "{Instance} movie {Title} was skipped on an error nobody expected", instance.DisplayName,
                movies[0].Movie.Title);
            run.Changes.Problem($"Kept {instance.DisplayName} movie {movies[0].Movie.Title} unchanged: {LibraryItems.Reason(exception)}");
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    private async Task UnlinkMissingAsync(MovieRun run, CancellationToken cancellationToken)
    {
        var instanceKey = run.Instance.Key;
        var linked = await _dbContext.MediaItems
            .Where(item => item.Type == MediaItemType.Movie && item.Links.Any(link => link.InstanceKey == instanceKey))
            .Select(item => item.IdentityKey)
            .ToListAsync(cancellationToken);

        foreach (var keys in linked.Where(key => !run.Seen.Contains(key)).Chunk(BatchSize))
        {
            var items = await LoadAsync(item => keys.Contains(item.IdentityKey), cancellationToken);
            var unlinked = Unlink(run, items.Values);
            await run.Changes.SaveAsync(_dbContext, unlinked, cancellationToken);
            _dbContext.ChangeTracker.Clear();
        }
    }

    private List<MediaItem> Unlink(MovieRun run, IEnumerable<MediaItem> items)
    {
        var instanceKey = run.Instance.Key;
        var unlinked = items.Where(item => LibraryItems.IsLinked(item, instanceKey) && !run.Seen.Contains(item.IdentityKey)).ToList();
        foreach (var item in unlinked)
        {
            LibraryItems.Unlink(_dbContext, item, instanceKey);
        }

        return unlinked;
    }

    private void Apply(MovieRun run, RadarrMovie movie, string key, Dictionary<string, MediaItem> items)
    {
        var instance = run.Instance;
        if (!items.TryGetValue(key, out var item))
        {
            item = new MediaItem
            {
                Type = MediaItemType.Movie,
                IdentityKey = key,
                Title = movie.Title!,
                SortTitle = movie.Title!
            };
            _dbContext.MediaItems.Add(item);
            items[key] = item;
        }

        LibraryItems.Link(_dbContext, item, instance.Key, movie.Id);
        if (LibraryItems.Describes(item, instance.Key, _options.Value))
        {
            Describe(item, movie, instance);
        }

        LibraryItems.AddedNoLaterThan(item, movie.Added);
        var file = movie.HasFile ? movie.MovieFile : null;
        if (!LibraryItems.SyncFile(_dbContext, item, instance.Key, file?.Id, file?.Path, file?.Size ?? 0, file?.DateAdded, run.Paths))
        {
            run.Changes.Problem($"Skipped the file of {instance.DisplayName} movie {movie.Title}: {file!.Path} is outside the path mappings or in the server's own folders.");
        }

        LibraryItems.SyncImages(_dbContext, item, instance.Key, movie.Id, movie.Images);
    }

    private void Describe(MediaItem item, RadarrMovie movie, ArrInstanceOptions instance)
    {
        var description = new ItemDescription(movie.Title!, movie.SortTitle, movie.Year, movie.Overview, movie.Runtime, movie.TmdbId,
            movie.ImdbId, movie.OriginalLanguage, movie.Genres, movie.Certification);
        if (!LibraryItems.Describe(item, description))
        {
            _logger.LogWarning("{Instance} language '{Language}' of {Title} has no ISO 639-2 code, original language left empty",
                instance.DisplayName, movie.OriginalLanguage?.Name, item.Title);
        }

        item.ImdbRating = LibraryItems.Score(movie.Ratings?.Imdb);
        item.ImdbVotes = movie.Ratings?.Imdb is { Votes: > 0 } imdb ? imdb.Votes : null;
        item.TmdbRating = LibraryItems.Score(movie.Ratings?.Tmdb);
        item.TrailerYoutubeId = LibraryItems.TrailerId(movie.YouTubeTrailerId);
    }

    /// <summary>
    /// Represents one sync of a Radarr instance. Seen holds the identity keys Radarr still has.
    /// </summary>
    private sealed record MovieRun(ArrInstanceOptions Instance, LibraryPaths Paths, SyncChanges Changes)
    {
        public HashSet<string> Seen { get; } = [];
        public int Skipped { get; set; }
    }
}
