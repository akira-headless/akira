using Microsoft.AspNetCore.Mvc;

namespace Akira.Server.Controllers;

/// <summary>
/// Represents the base of every controller under /api/v1.
/// </summary>
[ApiController]
public abstract class ApiController : ControllerBase;
