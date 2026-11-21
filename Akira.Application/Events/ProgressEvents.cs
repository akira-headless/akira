using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Akira.Server.Models.Events;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Akira.Server.Events;

/// <summary>
/// Sends playback.progress to the open connections of a session's user, at most once every five seconds per session. Like a
/// remote command it is never stored, so no catch-up, webhook or Trakt sees it.
/// </summary>
public sealed class ProgressEvents
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(5);

    // Sessions that never stop leave their entry behind; past this many the old ones go
    private const int MaxSessions = 256;
    private static readonly TimeSpan Forgotten = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _sent = new();
    private readonly EventHub _hub;
    private readonly IOptions<JsonOptions> _jsonOptions;
    private readonly TimeProvider _time;

    public ProgressEvents(
        EventHub hub,
        IOptions<JsonOptions> jsonOptions,
        TimeProvider time)
    {
        _hub = hub;
        _jsonOptions = jsonOptions;
        _time = time;
    }

    public void Publish(Guid userId, PlaybackPayload payload)
    {
        var now = _time.GetUtcNow();
        if (!Claim(payload.SessionId, now))
        {
            return;
        }

        if (_sent.Count > MaxSessions)
        {
            foreach (var (sessionId, sentAt) in _sent)
            {
                if (now - sentAt > Forgotten)
                {
                    _sent.TryRemove(sessionId, out _);
                }
            }
        }

        var options = _jsonOptions.Value.SerializerOptions;
        var message = new EventMessage(0, EventTypes.PlaybackProgress, 1, JsonSerializer.SerializeToElement(payload, options), now);
        _hub.SendToUser(userId, JsonSerializer.SerializeToUtf8Bytes(message, options));
    }

    public void Forget(Guid sessionId) => _sent.TryRemove(sessionId, out _);

    // Two reports at once send one event
    private bool Claim(Guid sessionId, DateTimeOffset now)
    {
        while (true)
        {
            if (_sent.TryGetValue(sessionId, out var last))
            {
                if (now - last < Every)
                {
                    return false;
                }

                if (_sent.TryUpdate(sessionId, now, last))
                {
                    return true;
                }
            }
            else if (_sent.TryAdd(sessionId, now))
            {
                return true;
            }
        }
    }
}
