namespace Akira.Server.Webhooks;

/// <summary>
/// Wakes the library sync worker before its interval, when a client asks to look for new movies and series.
/// </summary>
public sealed class LibrarySyncTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>
    /// Asks for a full sync. False when one is already waiting to start, which covers this request too.
    /// </summary>
    public bool Request()
    {
        try
        {
            _signal.Release();
            return true;
        }
        catch (SemaphoreFullException)
        {
            return false;
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}
