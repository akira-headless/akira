using System.Collections.Concurrent;

namespace Akira.Server.Services.Playback;

/// <summary>
/// Keeps the transcode job of a playing session for a moment. A player asks for a segment every few seconds, and each
/// request would otherwise read the file's tracks and lay out its keyframes again.
/// </summary>
public sealed class HlsStreamCache
{
    // Short, so a file that was replaced or probed again is followed within a few segments
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public HlsStreamCache(TimeProvider time)
    {
        _time = time;
    }

    // tracks names the file, the tracks and the decision of the session, so a track change is never answered with the old job
    internal HlsStream? Find(Guid sessionId, string tracks) =>
        _entries.TryGetValue(sessionId, out var entry) && entry.Tracks == tracks && entry.Until > _time.GetUtcNow() ? entry.Stream : null;

    internal void Keep(Guid sessionId, string tracks, HlsStream stream)
    {
        var now = _time.GetUtcNow();
        foreach (var (id, entry) in _entries)
        {
            if (entry.Until <= now)
            {
                _entries.TryRemove(id, out _);
            }
        }

        _entries[sessionId] = new Entry(tracks, stream, now + Lifetime);
    }

    /// <summary>
    /// Represents the job kept for one session until a time.
    /// </summary>
    private sealed record Entry(string Tracks, HlsStream Stream, DateTimeOffset Until);
}
