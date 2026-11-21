using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Playback;

namespace Akira.Server.Bridges;

/// <summary>
/// Keeps the viewings of files that progressive urls send as they are. A renderer reads such a file in ranges, opens a
/// new one for every seek and says nothing when it stops, so a viewing ends once no range was read for a while: stopped
/// where the ranges got to when it started, otherwise ended without a position.
/// </summary>
public sealed class DirectPlays
{
    // Long enough for the gap between the ranges of a seek, short enough that Now playing does not linger
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<(Guid File, Guid User, Guid Device), DirectPlay> _plays = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<DirectPlays> _logger;

    public DirectPlays(
        IServiceScopeFactory scopes,
        TimeProvider time,
        ILogger<DirectPlays> logger)
    {
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Joins the viewing of a file by a user on a device that is still going, for one more request; null when there is none.
    /// </summary>
    public DirectPlay? Join((Guid File, Guid User, Guid Device) key)
    {
        if (!_plays.TryGetValue(key, out var play))
        {
            return null;
        }

        lock (play)
        {
            if (play.Ended)
            {
                return null;
            }

            play.Active++;
            play.Quiet?.Cancel();
            play.Quiet = null;
            return play;
        }
    }

    /// <summary>
    /// Starts a viewing with its first request. When another request started one first, that one is joined and returned,
    /// and the caller ends the session it planned.
    /// </summary>
    public DirectPlay Add((Guid File, Guid User, Guid Device) key, Guid sessionId, ClaimsPrincipal user, long sizeBytes, long durationMs)
    {
        var play = new DirectPlay(key, sessionId, user, sizeBytes, durationMs) { Active = 1 };
        while (true)
        {
            if (_plays.TryAdd(key, play))
            {
                return play;
            }

            if (Join(key) is { } other)
            {
                return other;
            }

            // One that just ended, on its way out
            if (_plays.TryGetValue(key, out var ended))
            {
                _plays.TryRemove(new KeyValuePair<(Guid, Guid, Guid), DirectPlay>(key, ended));
            }
        }
    }

    /// <summary>
    /// Claims the start of a viewing; true for the one request that gets to start its session.
    /// </summary>
    public bool TryStart(DirectPlay play)
    {
        lock (play)
        {
            if (play.Started || play.Ended)
            {
                return false;
            }

            play.Started = true;
            return true;
        }
    }

    /// <summary>
    /// Ends one request of a viewing, with where it got to when it streamed long enough to count. The viewing ends a while
    /// after its last request, unless another one comes.
    /// </summary>
    public void Leave(DirectPlay play, long? positionMs)
    {
        CancellationTokenSource quiet;
        lock (play)
        {
            if (positionMs is { } position)
            {
                play.PositionMs = position;
            }

            play.Active--;
            if (play.Active > 0 || play.Ended)
            {
                return;
            }

            quiet = play.Quiet = new CancellationTokenSource();
        }

        _ = EndWhenQuietAsync(play, quiet.Token);
    }

    private async Task EndWhenQuietAsync(DirectPlay play, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Quiet, _time, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        bool started;
        long position;
        lock (play)
        {
            if (cancellationToken.IsCancellationRequested || play.Active > 0 || play.Ended)
            {
                return;
            }

            play.Ended = true;
            (started, position) = (play.Started, play.PositionMs ?? 0);
        }

        _plays.TryRemove(new KeyValuePair<(Guid, Guid, Guid), DirectPlay>(play.Key, play));
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var playback = scope.ServiceProvider.GetRequiredService<IPlaybackService>();
            if (started)
            {
                await playback.StopAsync(play.User, play.SessionId, new StopRequest(position), CancellationToken.None);
            }
            else
            {
                await playback.EndAsync(play.User, play.SessionId, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            // Nothing waits for this; the session is left to the stale session cleanup
            _logger.LogWarning(exception, "Ending direct playback session {SessionId} failed", play.SessionId);
        }
    }
}
