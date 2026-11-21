namespace Akira.Server.Events;

/// <summary>
/// Wakes the workers that read the event table, such as outgoing webhooks, as soon as events are stored.
/// </summary>
public sealed class EventSignal
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _next = NewSource();
    private long _version;

    // A reader takes it before reading the events table and waits with it, so a pulse in between is not lost
    public long Version => Interlocked.Read(ref _version);

    public void Pulse()
    {
        TaskCompletionSource current;
        lock (_lock)
        {
            Interlocked.Increment(ref _version);
            current = _next;
            _next = NewSource();
        }

        current.TrySetResult();
    }

    /// <summary>
    /// Waits for a pulse after the version the caller saw, or the timeout, whichever comes first; returns at once when
    /// one came already. Every waiter wakes on a pulse.
    /// </summary>
    public async Task WaitAsync(long seen, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task next;
        lock (_lock)
        {
            if (Interlocked.Read(ref _version) != seen)
            {
                return;
            }

            next = _next.Task;
        }

        try
        {
            await next.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
        }
    }

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
