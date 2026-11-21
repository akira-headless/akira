namespace Akira.Server.Authentication;

/// <summary>
/// Answers every API request with 403 "setup required" until the first admin exists.
/// </summary>
public sealed class SetupGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SetupState _setup;
    private readonly IProblemDetailsService _problemDetails;

    private static readonly string[] OpenPaths = ["/api/v1/setup", "/api/v1/health"];

    public SetupGateMiddleware(
        RequestDelegate next,
        SetupState setup,
        IProblemDetailsService problemDetails)
    {
        _next = next;
        _setup = setup;
        _problemDetails = problemDetails;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (_setup.IsComplete
            || !path.StartsWithSegments("/api")
            || OpenPaths.Any(open => path.StartsWithSegments(open)))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await _problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Title = "Setup required",
                Detail = "No users exist yet. Create the first admin with POST /api/v1/setup and the setup code from the server log.",
                Status = StatusCodes.Status403Forbidden,
                Extensions = { ["setupRequired"] = true }
            }
        });
    }
}
