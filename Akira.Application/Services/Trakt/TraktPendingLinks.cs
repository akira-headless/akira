using System.Collections.Concurrent;

namespace Akira.Server.Services.Trakt;

/// <summary>
/// Holds the device codes of links in progress, one per user. A restart forgets them, and the user starts again.
/// </summary>
public sealed class TraktPendingLinks
{
    /// <summary>
    /// Represents a Trakt device code waiting for the user to approve it at VerificationUrl.
    /// </summary>
    public sealed record Pending(string DeviceCode, string UserCode, string VerificationUrl, DateTimeOffset ExpiresAt, int IntervalSeconds);

    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();

    public void Set(Guid userId, Pending pending) => _pending[userId] = pending;

    public Pending? Get(Guid userId, DateTimeOffset now)
    {
        if (!_pending.TryGetValue(userId, out var pending))
        {
            return null;
        }

        if (pending.ExpiresAt > now)
        {
            return pending;
        }

        _pending.TryRemove(userId, out _);
        return null;
    }

    public void Remove(Guid userId) => _pending.TryRemove(userId, out _);
}
