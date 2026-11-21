namespace Akira.Server.Webhooks;

/// <summary>
/// Wakes the probe worker before its interval, so a file a webhook announced gets its tracks right away.
/// </summary>
public sealed class ProbeTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Request()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already requested, one pass covers both
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}
