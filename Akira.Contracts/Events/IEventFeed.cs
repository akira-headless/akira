namespace Akira.Contracts.Events;

/// <summary>
/// Akira's changes as clients see them, for a bridge that pushes them to its own clients. Per user changes only reach
/// that user.
/// </summary>
public interface IEventFeed
{
    /// <summary>
    /// Returns the changes after a sequence that the user may see, oldest first and at most limit, with Next to ask from
    /// the next time, which also moves past what was not for this user, has no kind here or is gone.
    /// </summary>
    Task<EventBatch> AfterAsync(Guid userId, long sequence, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the sequence of the newest stored event.
    /// </summary>
    Task<long> LatestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Waits until there are events after a sequence or the timeout passes, and returns at once when they are already
    /// there, so an event stored between reading and waiting is not missed.
    /// </summary>
    Task WaitAsync(long after, TimeSpan timeout, CancellationToken cancellationToken);
}
