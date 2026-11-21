using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Plugins;

namespace Akira.Server.Controllers;

[Route(ItemRoutes.ApiPrefix + "/plugins")]
[Tags("Plugins")]
[Authorize(Policy = AkiraPolicies.Admin)]
public sealed class PluginController : ApiController
{
    private readonly IPluginService _service;

    public PluginController(IPluginService service)
    {
        _service = service;
    }

    [HttpGet]
    [EndpointName("ListPlugins")]
    [EndpointSummary("Lists the plugins that loaded, built in or from the plugins folder, with their settings (never the values) and the DLLs that did not load.")]
    [ProducesResponseType<PluginOverview>(StatusCodes.Status200OK, "application/json")]
    public Ok<PluginOverview> ListPlugins() => TypedResults.Ok(_service.Overview());
}
