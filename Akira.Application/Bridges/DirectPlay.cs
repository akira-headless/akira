using System.Security.Claims;

namespace Akira.Server.Bridges;

/// <summary>
/// Represents one viewing of a file that a progressive url sends as it is: a renderer reads it in many ranges, and they
/// are all one session. Its state changes under a lock on itself.
/// </summary>
public sealed class DirectPlay
{
    internal DirectPlay((Guid File, Guid User, Guid Device) key, Guid sessionId, ClaimsPrincipal user, long sizeBytes, long durationMs)
    {
        Key = key;
        SessionId = sessionId;
        User = user;
        SizeBytes = sizeBytes;
        DurationMs = durationMs;
    }

    public Guid SessionId { get; }

    public ClaimsPrincipal User { get; }

    public long SizeBytes { get; }

    public long DurationMs { get; }

    internal (Guid File, Guid User, Guid Device) Key { get; }

    // Requests sending the file now
    internal int Active { get; set; }

    internal bool Started { get; set; }

    internal bool Ended { get; set; }

    internal long? PositionMs { get; set; }

    internal CancellationTokenSource? Quiet { get; set; }
}
