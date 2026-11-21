using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Playback;
using Akira.Server.Services;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix)]
[Tags("Track preferences")]
public sealed class TrackPreferenceController : ApiController
{
    private readonly ITrackPreferenceService _service;

    public TrackPreferenceController(ITrackPreferenceService service)
    {
        _service = service;
    }

    [HttpGet("items/{id:guid}/track-preferences")]
    [EndpointName("GetItemTrackPreferences")]
    [EndpointSummary("Retrieves the rules of a movie or episode, or of the whole series for a series or season, and what applies.")]
    public async Task<Results<Ok<ItemTrackPreferences>, ProblemHttpResult>> GetItemTrackPreferences(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetForItemAsync(User.GetUserId()!.Value, id, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpPut("items/{id:guid}/track-preferences")]
    [EndpointName("SetItemTrackPreferences")]
    [EndpointSummary("Sets those rules. A field left out falls back to the series or global rules.")]
    public async Task<Results<Ok<ItemTrackPreferences>, ProblemHttpResult>> SetItemTrackPreferences(Guid id, TrackRulesModel rules,
        CancellationToken cancellationToken)
    {
        var result = await _service.SetForItemAsync(User.GetUserId()!.Value, id, rules, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }

    [HttpDelete("items/{id:guid}/track-preferences")]
    [EndpointName("ResetItemTrackPreferences")]
    [EndpointSummary("Forgets those rules and the remembered tracks, so the global rules apply again.")]
    public async Task<Results<NoContent, ProblemHttpResult>> ResetItemTrackPreferences(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.ResetForItemAsync(User.GetUserId()!.Value, id, cancellationToken);
        return result.Succeeded ? TypedResults.NoContent() : ServiceResults.Problem(result, HttpContext);
    }

    [HttpGet("users/me/track-preferences")]
    [EndpointName("GetTrackPreferences")]
    [EndpointSummary("Retrieves your global rules and every series or item you set or remembered something for.")]
    public async Task<Ok<TrackPreferenceOverview>> GetTrackPreferences(CancellationToken cancellationToken)
    {
        var overview = await _service.GetOverviewAsync(User.GetUserId()!.Value, cancellationToken);
        return TypedResults.Ok(overview);
    }

    [HttpPut("users/me/track-preferences")]
    [EndpointName("SetGlobalTrackPreferences")]
    [EndpointSummary("Sets your global rules.")]
    public async Task<Results<Ok<TrackRulesModel>, ProblemHttpResult>> SetGlobalTrackPreferences(TrackRulesModel rules,
        CancellationToken cancellationToken)
    {
        var result = await _service.SetGlobalAsync(User.GetUserId()!.Value, rules, cancellationToken);
        return result.Succeeded ? TypedResults.Ok(result.Value) : ServiceResults.Problem(result, HttpContext);
    }
}
