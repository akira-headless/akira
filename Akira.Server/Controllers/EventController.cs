using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Events;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/events")]
[Tags("Events")]
// A shared device that has not picked a profile still hears about the library
[Authorize(Policy = AkiraPolicies.Client)]
public sealed class EventController : ApiController
{
    private readonly IEventFeedService _feed;
    private readonly EventSocket _socket;

    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    private const string EventsDescription = """
        Event types, with the schema of their payload:
        library.item.added, library.item.updated, library.item.removed (LibraryItemPayload),
        library.file.replaced (FileReplacedPayload), watchstate.changed (WatchStatePayload, only to that user),
        trackpreference.changed (TrackPreferencePayload, only to that user), scan.progress (ScanProgressPayload),
        action.completed (ActionCompletedPayload), subtitle.request.updated (SubtitleRequestModel, only to that user),
        playback.started, playback.stopped (PlaybackPayload, only to that user).
        playback.progress (PlaybackPayload, only to that user) is not in this list: the WebSocket alone sends it, with
        sequence 0, while a session plays, at most every 5 seconds per session.
        Page with since set to the sequence of the last item while hasMore is true, then continue from latest.
        When resync is true the events are gone: reload what is shown and continue from latest.
        """;

    public EventController(
        IEventFeedService feed,
        EventSocket socket)
    {
        _feed = feed;
        _socket = socket;
    }

    [HttpGet]
    [EndpointName("GetEvents")]
    [EndpointSummary("Lists the events after a sequence, for a client that was offline.")]
    [EndpointDescription(EventsDescription)]
    public async Task<Results<Ok<EventPage>, ValidationProblem>> GetEvents(
        [BindRequired] long since,
        CancellationToken cancellationToken,
        int limit = DefaultLimit)
    {
        var errors = new Dictionary<string, string[]>();
        if (since < 0)
        {
            errors["since"] = ["since cannot be negative."];
        }

        if (limit is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"limit must be between 1 and {MaxLimit}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var page = await _feed.GetSinceAsync(User.GetUserId(), since, limit, cancellationToken);
        return TypedResults.Ok(page);
    }

    [HttpGet("ws")]
    [EndpointName("ConnectEvents")]
    [EndpointSummary("Opens a WebSocket that replays the events after since, sends stream.ready, then every event as it happens.")]
    [EndpointDescription("""
        Messages have the shape of EventMessage. stream.ready carries the sequence the client is now at.
        stream.resync means the missed events are gone or too many: reload what is shown, the sequence is where to continue.
        Neither is stored. The server closes with 1001 when the client fell too far behind and with 1008 when its
        credential was revoked or expired; on 1001 or a dropped connection, reconnect with since set to the last sequence.
        With remote=true the device can be controlled from the caller's other devices (/remote): it then also gets
        remote.command messages (RemoteCommandPayload), sequence 0 and never stored, which it carries out. Every socket of a
        user gets playback.progress (PlaybackPayload), sequence 0 and never stored, at most every 5 seconds per playing session.
        """)]
    [ProducesResponseType(StatusCodes.Status101SwitchingProtocols)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<Results<EmptyHttpResult, ProblemHttpResult>> ConnectEvents(long? since, bool remote = false)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            return TypedResults.Problem(title: "Not a WebSocket request",
                detail: "Connect with a WebSocket, for example ws://server:8484/api/v1/events/ws?since=0.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (since < 0)
        {
            return TypedResults.Problem(title: "Invalid since", detail: "since cannot be negative.", statusCode: StatusCodes.Status400BadRequest);
        }

        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await _socket.RunAsync(webSocket, User, since, remote, HttpContext.RequestAborted);
        return TypedResults.Empty;
    }
}
