using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/api-keys")]
[Tags("API keys")]
public sealed class ApiKeyController : ApiController
{
    private readonly IApiKeyService _service;

    public ApiKeyController(IApiKeyService service)
    {
        _service = service;
    }

    [HttpGet]
    [EndpointName("ListApiKeys")]
    [EndpointSummary("Lists your API keys, or every key for an admin. Keys themselves are never shown again.")]
    public async Task<Ok<IReadOnlyList<ApiKeySummary>>> ListApiKeys(CancellationToken cancellationToken) =>
        TypedResults.Ok(await _service.ListAsync(User, cancellationToken));

    [HttpPost]
    [EndpointName("CreateApiKey")]
    [EndpointSummary("Creates an API key acting as you. The key is only in this response.")]
    public async Task<Results<Created<CreatedApiKey>, ProblemHttpResult>> CreateApiKey(CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(User, request, cancellationToken);
        return result.Succeeded
            ? TypedResults.Created($"{ItemRoutes.ApiPrefix}/api-keys/{result.Value!.Summary.Id}", result.Value)
            : ServiceResults.Problem(result, HttpContext);
    }

    [HttpDelete("{id:guid}")]
    [EndpointName("RevokeApiKey")]
    [EndpointSummary("Revokes an API key.")]
    public async Task<Results<NoContent, ProblemHttpResult>> RevokeApiKey(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RevokeAsync(User, id, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : ServiceResults.Problem(result, HttpContext);
    }
}
