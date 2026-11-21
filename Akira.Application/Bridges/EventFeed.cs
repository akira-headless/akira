using System.Text.Json;
using Akira.Contracts.Events;
using Akira.Server.Events;
using Akira.Server.Models.Events;
using Akira.Server.Interfaces.Services;

namespace Akira.Server.Bridges;

public sealed class EventFeed : IEventFeed
{
    private readonly IEventFeedService _events;
    private readonly EventSignal _signal;

    public EventFeed(
        IEventFeedService events,
        EventSignal signal)
    {
        _events = events;
        _signal = signal;
    }

    /// <inheritdoc />
    public async Task<EventBatch> AfterAsync(Guid userId, long sequence, int limit, CancellationToken cancellationToken)
    {
        var page = await _events.GetSinceAsync(userId, sequence, Math.Clamp(limit, 1, 500), cancellationToken);
        var items = page.Items.Select(ToEvent).OfType<AkiraEvent>().ToList();

        // On to the last event of a full page, else to the newest of all, which also gets a cursor past events that were deleted
        var next = page.HasMore && page.Items.Count > 0 ? page.Items[^1].Sequence : Math.Max(page.Latest, sequence);
        return new EventBatch(items, next);
    }

    // The contract has its own shape, so a change to the API's payloads never reaches a plugin. What has no kind there
    // (a scan, a track preference, a playback) is left out
    private static AkiraEvent? ToEvent(EventMessage message)
    {
        EventKind? kind = message.Type switch
        {
            EventTypes.ItemAdded => EventKind.ItemAdded,
            EventTypes.ItemUpdated => EventKind.ItemUpdated,
            EventTypes.ItemRemoved => EventKind.ItemRemoved,
            EventTypes.FileReplaced => EventKind.FileReplaced,
            EventTypes.WatchStateChanged => EventKind.WatchStateChanged,
            _ => null
        };
        if (kind is null
            || message.Payload.ValueKind != JsonValueKind.Object
            || !message.Payload.TryGetProperty("itemId", out var id)
            || !id.TryGetGuid(out var itemId))
        {
            return null;
        }

        return new AkiraEvent(message.Sequence, kind.Value, itemId, message.CreatedAt);
    }

    /// <inheritdoc />
    public Task<long> LatestAsync(CancellationToken cancellationToken) => _events.GetLatestAsync(cancellationToken);

    /// <inheritdoc />
    public async Task WaitAsync(long after, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var seen = _signal.Version;
        if (await _events.GetLatestAsync(cancellationToken) > after)
        {
            return;
        }

        await _signal.WaitAsync(seen, timeout, cancellationToken);
    }
}
