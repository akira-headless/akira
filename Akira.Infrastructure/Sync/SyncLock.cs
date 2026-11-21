namespace Akira.Infrastructure.Sync;

/// <summary>
/// Lets one sync write the library at a time.
/// </summary>
public sealed class SyncLock
{
    // A refresh storm could otherwise keep a full sync asking forever
    private const int MaxAttempts = 3;

    private long _version;

    // A webhook refresh and the scheduled full sync write the same rows. Run side by side they
    // would both add the same new episode and one of them would fail on the unique identity key.
    public SemaphoreSlim Gate { get; } = new(1, 1);

    // Radarr and Sonarr are asked before the gate is taken, so a slow answer holds nobody up. A version
    // that moved between asking and writing means another sync wrote meanwhile, maybe from a newer answer.
    public long Version => Interlocked.Read(ref _version);

    public long Advance() => Interlocked.Increment(ref _version);

    /// <summary>
    /// Takes the gate for an answer asked for at version, and asks again outside it while another sync wrote meanwhile.
    /// The caller holds the gate when this returns, and not when it throws.
    /// </summary>
    public async Task<T> EnterAsync<T>(long version, T answer, Func<Task<T>> askAgain, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await Gate.WaitAsync(cancellationToken);
            if (Version == version || attempt == MaxAttempts)
            {
                return answer;
            }

            Gate.Release();
            version = Version;
            answer = await askAgain();
        }
    }
}
