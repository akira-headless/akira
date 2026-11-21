using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Remote;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/remote")]
[Tags("Remote")]
public sealed class RemoteController : ApiController
{
    private readonly IRemoteService _service;

    public RemoteController(IRemoteService service)
    {
        _service = service;
    }

    [HttpGet("devices")]
    [EndpointName("ListRemoteDevices")]
    [EndpointSummary("Lists the caller's other devices that can be controlled now, with what each plays.")]
    [EndpointDescription("A device can be controlled while it has the event WebSocket open with remote=true, signed in as the caller.")]
    public async Task<Ok<IReadOnlyList<RemoteDevice>>> ListRemoteDevices(CancellationToken cancellationToken)
    {
        // A shared device that has not picked a profile acts for nobody, so it controls nothing
        if (User.GetUserId() is not { } userId)
        {
            return TypedResults.Ok((IReadOnlyList<RemoteDevice>)[]);
        }

        var devices = await _service.ListAsync(userId, User.GetDeviceId(), cancellationToken);
        return TypedResults.Ok(devices);
    }

    [HttpPost("devices/{deviceId:guid}/commands")]
    [EndpointName("SendRemoteCommand")]
    [EndpointSummary("Sends a command to one of the caller's devices: play an item, pause, resume, seek or stop.")]
    [EndpointDescription("""
        The device gets it at once as a remote.command message on its event WebSocket. It is not stored: a device that is
        not connected never gets it, which is a 404. play needs itemId (and fromStart to ignore the resume point), seek
        needs positionMs.
        """)]
    public async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> SendRemoteCommand(Guid deviceId, RemoteCommandRequest command,
        CancellationToken cancellationToken)
    {
        if (command.Type == RemoteCommandType.Play && command.ItemId is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["itemId"] = ["play needs an itemId."] });
        }

        if (command.Type == RemoteCommandType.Seek && command.PositionMs is not >= 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["positionMs"] = ["seek needs a positionMs of 0 or more."] });
        }

        var result = RemoteSendResult.NotConnected;
        if (User.GetUserId() is { } userId)
        {
            result = await _service.SendAsync(userId, User.GetDeviceId(), deviceId, command, cancellationToken);
        }

        return result switch
        {
            RemoteSendResult.Sent => TypedResults.NoContent(),
            RemoteSendResult.ItemNotFound => TypedResults.Problem(title: "Item not found", detail: "There is no item with that id.",
                statusCode: StatusCodes.Status404NotFound),
            _ => TypedResults.Problem(title: "Device not connected",
                detail: "That device is not one of yours, or it is not open and connected right now.", statusCode: StatusCodes.Status404NotFound),
        };
    }
}
