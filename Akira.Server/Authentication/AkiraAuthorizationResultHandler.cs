using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Akira.Server.Authentication;

/// <summary>
/// Writes 401 and 403 as problem details that say what the client has to do.
/// </summary>
public sealed class AkiraAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly IProblemDetailsService _problemDetails;

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public AkiraAuthorizationResultHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        string title, detail;
        int status;
        if (authorizeResult.Challenged)
        {
            status = StatusCodes.Status401Unauthorized;
            title = "Authentication required";
            detail = context.Items[AkiraAuthenticationHandler.FailureItem] as string
                     ?? "Send a device token as 'Authorization: Bearer <token>', or an API key as " +
                     $"'{AkiraAuthenticationHandler.ApiKeyHeader}: <key>'. A new device gets a token by pairing: " +
                     "POST /api/v1/auth/pairings.";
        }
        else
        {
            status = StatusCodes.Status403Forbidden;
            title = "Not allowed";
            detail = context.User.GetUserId() is null && context.User.GetDeviceId() is not null
                ? "This is a shared device without a profile. List profiles with GET /api/v1/auth/profiles and pick one " +
                  "with POST /api/v1/auth/profiles/{userId}/session."
                : "This needs an admin.";
        }

        context.Response.StatusCode = status;
        await _problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Title = title, Detail = detail, Status = status }
        });
    }
}
