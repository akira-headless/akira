using System.Collections.Concurrent;

namespace Akira.Server.Events;

/// <summary>
/// Hands stored events to every open WebSocket that may see them.
/// </summary>
public sealed class EventHub
{
    private readonly ConcurrentDictionary<EventSubscription, byte> _subscriptions = new();

    public int Connections => _subscriptions.Count;

    public EventSubscription Subscribe(Guid? userId, Guid? deviceId = null, bool remote = false)
    {
        var subscription = new EventSubscription(this, userId, deviceId, remote);
        _subscriptions[subscription] = 0;
        return subscription;
    }

    /// <summary>
    /// Lists the devices of a user that are connected now and take remote commands.
    /// </summary>
    public IReadOnlyList<Guid> RemoteDevices(Guid userId) =>
        _subscriptions.Keys
            .Where(s => s.Remote && s.UserId == userId && s.DeviceId is not null)
            .Select(s => s.DeviceId!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Sends a message to a device's open connections of that user only, without storing it; false when none is open.
    /// </summary>
    public bool SendToDevice(Guid userId, Guid deviceId, byte[] json)
    {
        var sent = false;
        foreach (var subscription in _subscriptions.Keys.Where(s => s.Remote && s.UserId == userId && s.DeviceId == deviceId))
        {
            subscription.Offer(new HubEvent(0, userId, json, Direct: true));
            sent = true;
        }

        return sent;
    }

    /// <summary>
    /// Sends a message to every open connection of a user, without storing it.
    /// </summary>
    public void SendToUser(Guid userId, byte[] json)
    {
        foreach (var subscription in _subscriptions.Keys.Where(s => s.UserId == userId))
        {
            subscription.Offer(new HubEvent(0, userId, json, Direct: true));
        }
    }

    public void Broadcast(IReadOnlyList<HubEvent> events)
    {
        foreach (var subscription in _subscriptions.Keys)
        {
            foreach (var hubEvent in events.Where(e => e.UserId is null || e.UserId == subscription.UserId))
            {
                subscription.Offer(hubEvent);
            }
        }
    }

    internal void Remove(EventSubscription subscription) => _subscriptions.TryRemove(subscription, out _);
}
