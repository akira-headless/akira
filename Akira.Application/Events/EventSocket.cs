using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Akira.Server.Authentication;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Akira.Server.Events;

/// <summary>
/// Runs one WebSocket: replays what the client missed, then sends events as they are published.
/// </summary>
public sealed class EventSocket
{
    // More missed events than this and reloading is cheaper for the client than replaying them
    public const int MaxReplay = 1000;
    private const int PageSize = 200;

    public static readonly TimeSpan CredentialCheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    private readonly EventHub _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<JsonOptions> _jsonOptions;
    private readonly TimeProvider _time;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<EventSocket> _logger;

    public EventSocket(
        EventHub hub,
        IServiceScopeFactory scopeFactory,
        IOptions<JsonOptions> jsonOptions,
        TimeProvider time,
        IHostApplicationLifetime lifetime,
        ILogger<EventSocket> logger)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _jsonOptions = jsonOptions;
        _time = time;
        _lifetime = lifetime;
        _logger = logger;
    }

    public async Task RunAsync(WebSocket socket, ClaimsPrincipal user, long? since, bool remote, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId();

        // Subscribed before reading the database, so nothing published in between is missed.
        // What arrives twice is skipped by its sequence.
        using var subscription = _hub.Subscribe(userId, user.GetDeviceId(), remote);

        // The server waits for an open socket when it stops, so stopping has to end it
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.ApplicationStopping);
        var receiving = ReceiveUntilClosedAsync(socket, connection);
        var revoked = new StrongBox<bool>();
        var watching = WatchCredentialAsync(user, revoked, connection);
        var close = (Status: WebSocketCloseStatus.NormalClosure, Reason: "");

        try
        {
            var cursor = await ReplayAsync(socket, userId, since, connection.Token);
            await SendAsync(socket, Control(EventTypes.StreamReady, cursor), connection.Token);

            while (await subscription.Reader.WaitToReadAsync(connection.Token))
            {
                while (subscription.Reader.TryRead(out var hubEvent))
                {
                    if (hubEvent.Direct)
                    {
                        await SendAsync(socket, hubEvent.Json, connection.Token);
                        continue;
                    }

                    if (hubEvent.Sequence <= cursor)
                    {
                        continue;
                    }

                    await SendAsync(socket, hubEvent.Json, connection.Token);
                    cursor = hubEvent.Sequence;
                }
            }

            if (subscription.Overflowed)
            {
                close = (WebSocketCloseStatus.EndpointUnavailable, $"Too far behind, reconnect with since={cursor}");
            }
        }
        catch (OperationCanceledException) when (revoked.Value)
        {
            close = (WebSocketCloseStatus.PolicyViolation, "The credential was revoked or expired");
        }
        catch (OperationCanceledException) when (_lifetime.ApplicationStopping.IsCancellationRequested)
        {
            close = (WebSocketCloseStatus.EndpointUnavailable, "The server is stopping");
        }
        catch (OperationCanceledException)
        {
            // The client closed
        }
        catch (Exception exception) when (exception is WebSocketException or IOException)
        {
            _logger.LogDebug(exception, "Event socket dropped");
        }
        finally
        {
            await connection.CancelAsync();
            await CloseAsync(socket, close.Status, close.Reason);
            var watchingEnded = watching.ContinueWith(_ => { }, TaskScheduler.Default);
            await Task.WhenAll(receiving, watchingEnded);
        }
    }

    private async Task<long> ReplayAsync(WebSocket socket, Guid? userId, long? since, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var feed = scope.ServiceProvider.GetRequiredService<IEventFeedService>();
        if (since is null)
        {
            return await feed.GetLatestAsync(cancellationToken);
        }

        var cursor = since.Value;
        var pages = new List<EventPage>();
        var count = 0;
        while (true)
        {
            var page = await feed.GetSinceAsync(userId, cursor, PageSize, cancellationToken);
            count += page.Items.Count;
            if (page.Resync || count > MaxReplay)
            {
                await SendAsync(socket, Control(EventTypes.StreamResync, page.Latest), cancellationToken);
                return page.Latest;
            }

            pages.Add(page);
            if (!page.HasMore)
            {
                break;
            }

            cursor = page.Items[^1].Sequence;
        }

        // Sent only once it is clear that replaying is the answer, so a client that has to reload never gets half a replay
        var options = _jsonOptions.Value.SerializerOptions;
        foreach (var message in pages.SelectMany(page => page.Items))
        {
            await SendAsync(socket, JsonSerializer.SerializeToUtf8Bytes(message, options), cancellationToken);
        }

        return pages[^1].Latest;
    }

    private async Task WatchCredentialAsync(ClaimsPrincipal user, StrongBox<bool> revoked, CancellationTokenSource connection)
    {
        // Revoking a device or deleting a key also ends its open connection, within a minute
        using var timer = new PeriodicTimer(CredentialCheckInterval, _time);
        while (await timer.WaitForNextTickAsync(connection.Token))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var credentialCheck = scope.ServiceProvider.GetRequiredService<CredentialCheck>();
                if (!await credentialCheck.IsValidAsync(user, connection.Token))
                {
                    revoked.Value = true;
                    await connection.CancelAsync();
                    return;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A database that is away for a moment must not end the checks, or a device revoked later keeps its socket
                _logger.LogWarning("Could not check the credential of an event socket: {Message}", exception.Message);
            }
        }
    }

    private async Task ReceiveUntilClosedAsync(WebSocket socket, CancellationTokenSource connection)
    {
        // Clients send nothing, but reading is how a close or a dropped connection shows up
        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, connection.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
        {
        }

        await connection.CancelAsync();
    }

    private static async Task SendAsync(WebSocket socket, byte[] json, CancellationToken cancellationToken)
    {
        // A client that stops reading must not hold the connection open forever
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SendTimeout);
        await socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await socket.CloseOutputAsync(status, reason, timeout.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
        {
        }
    }

    private byte[] Control(string type, long sequence)
    {
        var options = _jsonOptions.Value.SerializerOptions;
        var payload = JsonSerializer.SerializeToElement(new StreamPayload(sequence), options);
        var message = new EventMessage(sequence, type, 1, payload, _time.GetUtcNow());
        return JsonSerializer.SerializeToUtf8Bytes(message, options);
    }
}
