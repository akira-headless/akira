using System.Threading.Channels;

namespace Akira.Server.Events;

public sealed class EventSubscription : IDisposable
{
    // Far more than a client falls behind while it is connected. One that does is closed and
    // catches up from the database after reconnecting, rather than holding events in memory.
    public const int Capacity = 1000;

    private readonly EventHub _hub;
    private readonly Channel<HubEvent> _channel = Channel.CreateBounded<HubEvent>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    internal EventSubscription(EventHub hub, Guid? userId, Guid? deviceId, bool remote)
    {
        _hub = hub;
        UserId = userId;
        DeviceId = deviceId;
        Remote = remote;
    }

    public Guid? UserId { get; }

    public Guid? DeviceId { get; }

    public bool Remote { get; }

    public bool Overflowed { get; private set; }

    public ChannelReader<HubEvent> Reader => _channel.Reader;

    internal void Offer(HubEvent hubEvent)
    {
        if (!_channel.Writer.TryWrite(hubEvent))
        {
            Overflowed = true;
            _channel.Writer.TryComplete();
        }
    }

    public void Dispose() => _hub.Remove(this);
}
