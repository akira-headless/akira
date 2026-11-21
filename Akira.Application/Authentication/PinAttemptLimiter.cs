using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Akira.Server.Authentication;

/// <summary>
/// Counts PIN attempts per device and user, and locks that pair out after too many wrong ones, longer each time.
/// </summary>
public sealed class PinAttemptLimiter
{
    private static readonly TimeSpan MaxLockout = TimeSpan.FromDays(1);

    // In memory on purpose: one server, and a restart clearing a lockout only hands out one more round of attempts.
    // Signing in with a PIN needs no paired device, only an address inside the house, so the lockouts have to hold.
    private readonly ConcurrentDictionary<(Guid DeviceId, Guid UserId), Attempts> _attempts = new();

    private readonly IOptions<AuthOptions> _options;
    private readonly TimeProvider _time;

    public PinAttemptLimiter(
        IOptions<AuthOptions> options,
        TimeProvider time)
    {
        _options = options;
        _time = time;
    }

    /// <summary>
    /// Counts an attempt before its PIN is checked, so attempts sent together cannot all get past the lock. Returns
    /// false when the pair is locked out. Remaining is how many attempts are left after this one.
    /// </summary>
    public bool TryBegin(Guid deviceId, Guid userId, out int remaining, out TimeSpan lockedFor)
    {
        var settings = _options.Value;
        var now = _time.GetUtcNow();
        var admitted = true;

        Attempts Next(int failures, int lockouts) => failures >= settings.PinMaxAttempts
            ? new Attempts(failures, now + LockoutAfter(lockouts), lockouts + 1)
            : new Attempts(failures, null, lockouts);

        var updated = _attempts.AddOrUpdate((deviceId, userId),
            _ => Next(1, 0),
            (_, current) =>
            {
                admitted = current.LockedUntil is null || current.LockedUntil <= now;
                if (!admitted)
                {
                    return current;
                }

                // A lockout that has run out starts counting from zero again
                return Next(current.LockedUntil is null ? current.Failures + 1 : 1, current.Lockouts);
            });

        remaining = Math.Max(0, settings.PinMaxAttempts - updated.Failures);
        lockedFor = updated.LockedUntil is { } until && until > now ? until - now : TimeSpan.Zero;
        return admitted;
    }

    // Each lockout in a row takes twice as long, so guessing a 4 digit PIN five at a time takes years, not days
    private TimeSpan LockoutAfter(int lockouts)
    {
        var doubled = _options.Value.PinLockout * Math.Pow(2, Math.Min(lockouts, 16));
        return doubled < MaxLockout ? doubled : MaxLockout;
    }

    public void Reset(Guid deviceId, Guid userId) => _attempts.TryRemove((deviceId, userId), out _);

    /// <summary>
    /// Represents the PIN attempts of one device and user. LockedUntil is null while that pair is not locked out,
    /// Lockouts counts the lockouts since the last right PIN.
    /// </summary>
    private sealed record Attempts(int Failures, DateTimeOffset? LockedUntil, int Lockouts);
}
