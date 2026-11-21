using Akira.Contracts.Bridges;
using Microsoft.AspNetCore.Mvc;

namespace Akira.Server.Tests.Plugins;

// Asks for a route of the API; the host keeps it under the bridge's path
public sealed class ProbeController : BridgeController<ProbeBridge>
{
    [HttpGet("/api/v1/escape")]
    public string Escape() => "escaped";
}
