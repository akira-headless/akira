using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Subtitles;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix)]
[Tags("Subtitle requests")]
public sealed class SubtitleController : ApiController
{
    private readonly ISubtitleService _service;

    public SubtitleController(ISubtitleService service)
    {
        _service = service;
    }

    [HttpGet("items/{id:guid}/subtitle-options")]
    [EndpointName("GetSubtitleOptions")]
    [EndpointSummary("Lists what can be done for a language: tracks the version has, downloads, and tracks to translate from.")]
    [EndpointDescription("fileId picks the version, the first one otherwise. Sources that fail are listed in problems, the rest still answers.")]
    public async Task<Results<Ok<SubtitleOptionsResponse>, ProblemHttpResult>> GetSubtitleOptions(
        Guid id,
        CancellationToken cancellationToken,
        string? language = null,
        Guid? fileId = null)
    {
        var result = await _service.GetOptionsAsync(id, language, fileId, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPost("items/{id:guid}/subtitle-requests")]
    [EndpointName("RequestSubtitle")]
    [EndpointSummary("Asks for a subtitle the file does not have yet. Progress arrives as subtitle.request.updated events.")]
    [EndpointDescription("""
        optionId is one from subtitle-options; without it the server picks a track the file has, then a download, then a
        translation. sessionId ties the result to a playback, so the finished event carries the track to load into it.
        An open request for the same file and language is returned instead of starting a second one.
        """)]
    public async Task<Results<Accepted<SubtitleRequestModel>, ProblemHttpResult>> RequestSubtitle(Guid id, CreateSubtitleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(User, id, request, cancellationToken);
        return result.Succeeded
            ? TypedResults.Accepted($"/api/v1/subtitle-requests/{result.Value!.Id}", result.Value)
            : ServiceResults.Problem(result, HttpContext);
    }

    [HttpGet("subtitle-requests")]
    [EndpointName("ListSubtitleRequests")]
    [EndpointSummary("Lists your latest subtitle requests, or everyone's for an admin.")]
    public async Task<Ok<IReadOnlyList<SubtitleRequestModel>>> ListSubtitleRequests(CancellationToken cancellationToken, Guid? itemId) =>
        TypedResults.Ok(await _service.ListAsync(User, itemId, cancellationToken));

    [HttpGet("subtitle-requests/{id:guid}")]
    [EndpointName("GetSubtitleRequest")]
    [EndpointSummary("Retrieves a subtitle request with its progress, and the new track once it is done.")]
    public async Task<Results<Ok<SubtitleRequestModel>, ProblemHttpResult>> GetSubtitleRequest(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(User, id, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPost("subtitle-requests/{id:guid}/cancel")]
    [EndpointName("CancelSubtitleRequest")]
    [EndpointSummary("Stops a queued or running subtitle request.")]
    public async Task<Results<Ok<SubtitleRequestModel>, ProblemHttpResult>> CancelSubtitleRequest(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(User, id, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpGet("subtitle-providers")]
    [EndpointName("ListSubtitleProviders")]
    [EndpointSummary("Lists the subtitle sources and translators and whether each is configured.")]
    public Ok<IReadOnlyList<SubtitleProviderModel>> ListSubtitleProviders() => TypedResults.Ok(_service.Providers());
}
