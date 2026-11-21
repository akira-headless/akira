using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Akira.Server.Authentication;

/// <summary>
/// Limits the pairings one address can start within a pairing's lifetime, from the API and from the bridges.
/// The pending pairings are capped for the whole server, so without it one caller could keep them full and no device in the house could pair.
/// </summary>
public sealed class PairingStartLimiter : IDisposable
{
    public const int PerSource = 5;

    private readonly PartitionedRateLimiter<string> _limiter;

    public PairingStartLimiter(IOptions<AuthOptions> options)
    {
        _limiter = PartitionedRateLimiter.Create<string, string>(source =>
            RateLimitPartition.GetSlidingWindowLimiter(source, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = PerSource,
                Window = options.Value.PairingLifetime,
                SegmentsPerWindow = 10,
                QueueLimit = 0
            }));
    }

    public bool TryStart(IPAddress? from)
    {
        using var lease = _limiter.AttemptAcquire(ClientAddresses.PartitionKey(from));
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
