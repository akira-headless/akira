namespace Akira.Server.Services.Subtitles;

/// <summary>
/// Wakes the subtitle worker as soon as a request is made, instead of at its next round.
/// </summary>
public sealed class SubtitleRequestTrigger
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
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}
