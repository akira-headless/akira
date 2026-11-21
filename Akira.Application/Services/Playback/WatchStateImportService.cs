using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Library;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using Akira.Server.Models.Playback;
using Akira.Server.Playback;

namespace Akira.Server.Services.Playback;

public sealed class WatchStateImportService : IWatchStateImportService
{
    public const int MaxEntries = 1000;

    private readonly AkiraDbContext _dbContext;
    private readonly IEventPublisher _events;
    private readonly IOptions<PlaybackOptions> _options;
    private readonly ILogger<WatchStateImportService> _logger;

    public WatchStateImportService(
        AkiraDbContext dbContext,
        IEventPublisher events,
        IOptions<PlaybackOptions> options,
        ILogger<WatchStateImportService> logger)
    {
        _dbContext = dbContext;
        _events = events;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<WatchStateImportResult>> ImportAsync(Guid userId, WatchStateImportRequest request,
        CancellationToken cancellationToken)
    {
        var entries = request.Entries ?? [];
        if (entries.Count is 0 or > MaxEntries)
        {
            return ServiceResult<WatchStateImportResult>.Fail(ServiceError.Invalid, $"entries holds 1 to {MaxEntries} entries.");
        }

        var keys = await KeysAsync(entries, cancellationToken);
        var matched = keys.Where(key => key is not null).Select(key => key!).Distinct().ToList();
        var states = await _dbContext.WatchStates
            .Where(w => w.UserId == userId && matched.Contains(w.IdentityKey))
            .ToDictionaryAsync(w => w.IdentityKey, cancellationToken);

        var minimum = (long)_options.Value.MinimumResume.TotalMilliseconds;
        var changed = new HashSet<string>();
        var unchanged = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            if (keys[index] is not { } key)
            {
                continue;
            }

            var created = !states.TryGetValue(key, out var state);
            state ??= new WatchState { UserId = userId, IdentityKey = key };
            if (!Apply(state, entries[index], created, minimum))
            {
                unchanged++;
                continue;
            }

            if (created)
            {
                states[key] = state;
                if (!request.DryRun)
                {
                    _dbContext.WatchStates.Add(state);
                }
            }

            changed.Add(key);
        }

        var unmatched = Enumerable.Range(0, entries.Count).Where(index => keys[index] is null).ToList();
        if (!request.DryRun && changed.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await PublishAsync(userId, changed, states, cancellationToken);
            _logger.LogInformation("Imported watch state for user {UserId}: {Updated} updated, {Unchanged} unchanged, {Unmatched} without ids",
                userId, changed.Count, unchanged, unmatched.Count);
        }

        var result = new WatchStateImportResult(entries.Count, changed.Count, unchanged, unmatched, request.DryRun);
        return ServiceResult<WatchStateImportResult>.Ok(result);
    }

    // Played only ever adds: an import never makes something unplayed, and a position only lands on an item that is not
    // played here and was not watched here more recently than on the other server
    private static bool Apply(WatchState state, WatchStateImportEntry entry, bool created, long minimum)
    {
        var before = (state.IsPlayed, state.PositionMs, state.PlayCount, state.LastPlayedAt);
        if (entry.Played)
        {
            if (!state.IsPlayed)
            {
                state.IsPlayed = true;
                state.PositionMs = 0;
            }

            var importedCount = Math.Max(entry.PlayCount ?? 1, 1);
            state.PlayCount = Math.Max(state.PlayCount, importedCount);
            state.LastPlayedAt = Later(state.LastPlayedAt, entry.LastPlayedAt);
        }
        else if (entry.PositionMs is { } position && position >= minimum && !state.IsPlayed
                 && (created || state.PositionMs == 0 || entry.LastPlayedAt > (state.LastPlayedAt ?? state.UpdatedAt)))
        {
            state.PositionMs = position;
            state.LastPlayedAt = Later(state.LastPlayedAt, entry.LastPlayedAt);
        }

        return before != (state.IsPlayed, state.PositionMs, state.PlayCount, state.LastPlayedAt);
    }

    private static DateTimeOffset? Later(DateTimeOffset? current, DateTimeOffset? imported)
    {
        if (imported is { } value && (current is null || value > current))
        {
            return value;
        }

        return current;
    }

    // TMDB keys a movie and TVDB a series by themselves. Other ids are looked up in the library: a movie known here by its
    // IMDb id, a series by its TMDB or IMDb id, which is how other servers often know them.
    private async Task<List<string?>> KeysAsync(IReadOnlyList<WatchStateImportEntry> entries, CancellationToken cancellationToken)
    {
        var imdbIds = entries
            .Select(entry => entry.ImdbId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct()
            .ToList();
        var seriesTmdbIds = entries
            .Where(entry => entry.Kind == WatchStateImportKind.Episode && entry.TmdbId > 0)
            .Select(entry => entry.TmdbId!.Value)
            .Distinct()
            .ToList();
        var known = await _dbContext.MediaItems.AsNoTracking()
            .Where(item => (item.Type == MediaItemType.Movie || item.Type == MediaItemType.Series)
                           && ((item.ImdbId != null && imdbIds.Contains(item.ImdbId))
                               || (item.Type == MediaItemType.Series && item.TmdbId != null && seriesTmdbIds.Contains(item.TmdbId.Value))))
            .Select(item => new { item.Type, item.ImdbId, item.TmdbId, item.IdentityKey })
            .ToListAsync(cancellationToken);

        string? Find(MediaItemType type, int? tmdbId, string? imdbId)
        {
            var byImdb = known.FirstOrDefault(item => item.Type == type && imdbId != null && item.ImdbId == imdbId.Trim())?.IdentityKey;
            if (byImdb is not null)
            {
                return byImdb;
            }

            if (tmdbId > 0)
            {
                return known.FirstOrDefault(item => item.Type == type && item.TmdbId == tmdbId)?.IdentityKey;
            }

            return null;
        }

        string? MovieKey(WatchStateImportEntry entry)
        {
            if (entry.TmdbId > 0)
            {
                return IdentityKeys.Movie(entry.TmdbId, "", 0);
            }

            return Find(MediaItemType.Movie, null, entry.ImdbId);
        }

        string? EpisodeKey(WatchStateImportEntry entry)
        {
            var series = entry.TvdbId > 0 ? IdentityKeys.Series(entry.TvdbId, "", 0) : Find(MediaItemType.Series, entry.TmdbId, entry.ImdbId);
            if (series is null)
            {
                return null;
            }

            return IdentityKeys.Episode(series, entry.SeasonNumber!.Value, entry.EpisodeNumber!.Value);
        }

        return entries.Select(entry => entry.Kind switch
        {
            WatchStateImportKind.Movie => MovieKey(entry),
            WatchStateImportKind.Episode when entry is { SeasonNumber: >= 0, EpisodeNumber: > 0 } => EpisodeKey(entry),
            _ => null
        }).ToList();
    }

    // Clients refresh what they show from these; a key with no item here yet has nothing on screen to refresh
    private async Task PublishAsync(Guid userId, IReadOnlyCollection<string> keys, Dictionary<string, WatchState> states,
        CancellationToken cancellationToken)
    {
        var items = await _dbContext.MediaItems.AsNoTracking()
            .Where(item => keys.Contains(item.IdentityKey))
            .Select(item => new { item.Id, item.IdentityKey })
            .ToListAsync(cancellationToken);
        var drafts = items.Select(item =>
        {
            var state = states[item.IdentityKey];
            var payload = new WatchStatePayload(item.Id, item.IdentityKey, state.PositionMs, state.IsPlayed, state.PlayCount,
                state.LastPlayedAt, WatchStateReasons.Imported);
            return new EventDraft(EventTypes.WatchStateChanged, payload, userId);
        }).ToList();
        await _events.PublishAsync(drafts);
    }
}
