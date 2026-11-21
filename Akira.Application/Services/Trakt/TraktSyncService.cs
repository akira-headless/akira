using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Entities;
using Akira.Server.Events;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Trakt;
using Akira.Server.Models.Events;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Akira.Server.Services.Trakt;

/// <summary>
/// Reads the event table after its cursor: playback start and stop become scrobbles, played or unplayed by hand becomes history.
/// </summary>
public sealed class TraktSyncService : ITraktSyncService
{
    public const string Consumer = "trakt";

    // Scrobbles are sent once, also when history that Trakt could not take holds the other cursor back
    public const string ScrobbleConsumer = "trakt-scrobbles";
    private const int BatchSize = 200;

    private readonly AkiraDbContext _dbContext;
    private readonly ITraktClient _trakt;
    private readonly TraktTokens _tokens;
    private readonly IOptions<JsonOptions> _jsonOptions;
    private readonly TimeProvider _time;
    private readonly ILogger<TraktSyncService> _logger;

    public TraktSyncService(
        AkiraDbContext dbContext,
        ITraktClient trakt,
        TraktTokens tokens,
        IOptions<JsonOptions> jsonOptions,
        TimeProvider time,
        ILogger<TraktSyncService> logger)
    {
        _dbContext = dbContext;
        _trakt = trakt;
        _tokens = tokens;
        _jsonOptions = jsonOptions;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SyncPendingAsync(CancellationToken cancellationToken)
    {
        var cursor = await _dbContext.EventCursors.SingleOrDefaultAsync(c => c.Consumer == Consumer, cancellationToken);
        if (cursor is null)
        {
            // From now on: what happened before Trakt sync existed is in the import, not replayed as scrobbles
            var lastSequence = await _dbContext.Events.MaxAsync(e => (long?)e.Sequence, cancellationToken);
            cursor = new EventCursor { Consumer = Consumer, LastSequence = lastSequence ?? 0 };
            _dbContext.EventCursors.Add(cursor);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        var events = await _dbContext.Events.AsNoTracking()
            .Where(e => e.Sequence > cursor.LastSequence)
            .OrderBy(e => e.Sequence)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (events.Count == 0)
        {
            return false;
        }

        var scrobbles = await _dbContext.EventCursors.SingleOrDefaultAsync(c => c.Consumer == ScrobbleConsumer, cancellationToken);
        if (scrobbles is null)
        {
            scrobbles = new EventCursor { Consumer = ScrobbleConsumer, LastSequence = cursor.LastSequence };
            _dbContext.EventCursors.Add(scrobbles);
        }

        var userIds = events.Where(e => e.UserId != null).Select(e => e.UserId!.Value).Distinct().ToList();
        var links = await _dbContext.TraktLinks
            .Where(l => userIds.Contains(l.UserId))
            .ToDictionaryAsync(l => l.UserId, cancellationToken);
        var marked = new List<(long Sequence, TraktLink Link, TraktItem Item, bool Played, DateTimeOffset? At)>();

        foreach (var row in events)
        {
            if (row.UserId is not { } userId || !links.TryGetValue(userId, out var link))
            {
                continue;
            }

            try
            {
                switch (row.Type)
                {
                    case EventTypes.PlaybackStarted or EventTypes.PlaybackStopped when row.Sequence > scrobbles.LastSequence:
                        var playback = Read<PlaybackPayload>(row);
                        if (TraktItems.FromIdentityKey(playback.IdentityKey) is { } item)
                        {
                            var action = row.Type == EventTypes.PlaybackStarted ? "start" : "stop";
                            await ScrobbleAsync(link, item, playback, action, cancellationToken);
                        }

                        break;
                    case EventTypes.WatchStateChanged:
                        var state = Read<WatchStatePayload>(row);
                        if (state.Reason == WatchStateReasons.Marked && TraktItems.FromIdentityKey(state.IdentityKey) is { } markedItem)
                        {
                            marked.Add((row.Sequence, link, markedItem, state.IsPlayed, state.LastPlayedAt));
                        }

                        break;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // An event that cannot be read would come back every round and hold up everything behind it
                _logger.LogWarning("Trakt sync skipped event {Sequence}: {Reason}", row.Sequence, exception.Message);
            }
        }

        // A Trakt that is down loses these scrobbles: they only mean something while the user watches
        scrobbles.LastSequence = events[^1].Sequence;

        // Only the last mark of an item counts. They go out in the order they were made, one call for each run of
        // the same user and direction, so a call Trakt could not take holds back exactly what comes after it.
        var latest = marked.GroupBy(entry => (entry.Link, entry.Item)).Select(entries => entries.Last()).OrderBy(entry => entry.Sequence).ToList();
        var handled = events[^1].Sequence;
        for (var start = 0; start < latest.Count;)
        {
            var first = latest[start];
            var end = start + 1;
            while (end < latest.Count && latest[end].Link == first.Link && latest[end].Played == first.Played)
            {
                end++;
            }

            var run = latest[start..end];
            var sent = await Run(first.Link, async accessToken =>
            {
                if (first.Played)
                {
                    var watched = run.Select(e => (e.Item, e.At ?? _time.GetUtcNow()));
                    await _trakt.AddHistoryAsync(accessToken, TraktItems.HistoryBody(watched), cancellationToken);
                }
                else
                {
                    await _trakt.RemoveHistoryAsync(accessToken, TraktItems.RemoveBody(run.Select(e => e.Item)), cancellationToken);
                }
            }, cancellationToken);
            if (!sent)
            {
                // History is not lost like a scrobble: the next round starts again at this mark
                handled = first.Sequence - 1;
                break;
            }

            start = end;
        }

        cursor.LastSequence = handled;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return handled == events[^1].Sequence && events.Count == BatchSize;
    }

    private async Task ScrobbleAsync(TraktLink link, TraktItem item, PlaybackPayload playback, string action, CancellationToken cancellationToken)
    {
        var duration = await _dbContext.MediaFiles
            .Where(f => f.Id == playback.FileId)
            .Select(f => f.DurationMs)
            .SingleOrDefaultAsync(cancellationToken);
        var progress = duration is > 0 ? playback.PositionMs * 100.0 / duration.Value : 0;
        await Run(link, accessToken =>
        {
            var body = TraktItems.ScrobbleBody(item, progress);
            return _trakt.ScrobbleAsync(accessToken, action, body, cancellationToken);
        }, cancellationToken);
    }

    // Returns false when Trakt was away for a moment, so the same call is worth another try later
    private async Task<bool> Run(TraktLink link, Func<string, Task> call, CancellationToken cancellationToken)
    {
        try
        {
            var accessToken = await _tokens.AccessTokenAsync(link, cancellationToken);
            await call(accessToken);
            link.LastSyncedAt = _time.GetUtcNow();
            link.LastError = null;
            return true;
        }
        catch (Exception exception) when (exception is UpstreamException or CryptographicException)
        {
            link.LastError = exception is UpstreamException { Failure: UpstreamFailure.Unauthorized }
                ? "Trakt refused the token. Link the account again."
                : exception.Message;
            _logger.LogWarning("Trakt sync for {Username} failed: {Reason}", link.Username, exception.Message);
            return exception is not UpstreamException
            {
                Failure: UpstreamFailure.Timeout or UpstreamFailure.Unreachable or UpstreamFailure.RateLimited or UpstreamFailure.UnexpectedStatus
            };
        }
    }

    private T Read<T>(OutboxEvent row) => JsonSerializer.Deserialize<T>(row.Payload, _jsonOptions.Value.SerializerOptions)!;
}
