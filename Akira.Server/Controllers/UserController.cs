using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/users")]
[Tags("Users")]
public sealed class UserController : ApiController
{
    private readonly IUserService _service;

    public UserController(IUserService service)
    {
        _service = service;
    }

    [HttpGet("me")]
    [EndpointName("GetCurrentUser")]
    [EndpointSummary("Retrieves the signed in user and the device the request came from.")]
    public async Task<Results<Ok<CurrentUser>, ProblemHttpResult>> GetCurrentUser(CancellationToken cancellationToken)
    {
        var result = await _service.GetCurrentAsync(User, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPut("me/pin")]
    [EndpointName("SetOwnPin")]
    [EndpointSummary("Sets, changes or removes your own PIN. Changing an existing PIN needs the current one.")]
    public Task<Results<Ok<UserSummary>, ProblemHttpResult>> SetOwnPin(SetPinRequest request, CancellationToken cancellationToken) =>
        SetPin(User.GetUserId()!.Value, request, cancellationToken);

    [HttpGet]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("ListUsers")]
    [EndpointSummary("Lists every user.")]
    public async Task<Ok<IReadOnlyList<UserSummary>>> ListUsers(CancellationToken cancellationToken) =>
        TypedResults.Ok(await _service.ListAsync(cancellationToken));

    [HttpPost]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("CreateUser")]
    [EndpointSummary("Creates a user.")]
    public async Task<Results<Created<UserSummary>, ProblemHttpResult>> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return result.Succeeded
            ? TypedResults.Created($"{ItemRoutes.ApiPrefix}/users/{result.Value!.Id}", result.Value)
            : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("UpdateUser")]
    [EndpointSummary("Renames a user or changes their role.")]
    public async Task<Results<Ok<UserSummary>, ProblemHttpResult>> UpdateUser(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("DeleteUser")]
    [EndpointSummary("Deletes a user with their devices, sessions and API keys.")]
    public async Task<Results<NoContent, ProblemHttpResult>> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(id, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPut("{id:guid}/pin")]
    [Authorize(Policy = AkiraPolicies.Admin)]
    [EndpointName("SetUserPin")]
    [EndpointSummary("Sets or removes a user's PIN.")]
    public Task<Results<Ok<UserSummary>, ProblemHttpResult>> SetUserPin(Guid id, SetPinRequest request, CancellationToken cancellationToken) =>
        SetPin(id, request, cancellationToken);

    private async Task<Results<Ok<UserSummary>, ProblemHttpResult>> SetPin(Guid userId, SetPinRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.SetPinAsync(User, userId, request, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }
}
