using Akira.Server.Models.Events;

namespace Akira.Server.Interfaces.Services;

public interface IEventFeedService
{
    /// <summary>
    /// Retrieves the events after a sequence that a user may see, or tells the client to reload when they are gone.
    /// </summary>
    Task<EventPage> GetSinceAsync(Guid? userId, long since, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the sequence of the newest stored event, 0 when there is none.
    /// </summary>
    Task<long> GetLatestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes events older than the retention, always keeping the newest so sequences never restart.
    /// </summary>
    Task<int> PruneAsync(CancellationToken cancellationToken);
}
