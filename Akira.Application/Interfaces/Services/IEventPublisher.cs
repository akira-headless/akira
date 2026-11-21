using Akira.Server.Events;

namespace Akira.Server.Interfaces.Services;

public interface IEventPublisher
{
    /// <summary>
    /// Stores events in order and sends them to the connected clients that may see them. Not cancellable,
    /// because the change an event describes is already saved.
    /// </summary>
    Task PublishAsync(IReadOnlyList<EventDraft> events);
}
