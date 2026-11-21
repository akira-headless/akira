using Microsoft.AspNetCore.Mvc;

namespace Akira.Server.Tests.Plugins;

// Not a bridge's controller, so a plugin may not have it at all
[Route("api/v1/sneak")]
public sealed class SneakController : ControllerBase
{
    [HttpGet]
    public string Sneak() => "sneaked";
}
