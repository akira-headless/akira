using System.Security.Claims;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IBrowserSessionService
{
    /// <summary>
    /// Issues the token a browser keeps in its session cookie, for a new device from an API key or for the paired device itself.
    /// </summary>
    Task<ServiceResult<BrowserSession>> StartAsync(ClaimsPrincipal principal, BrowserSessionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the device of a browser that signs out. Other credentials are left alone.
    /// </summary>
    Task EndAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
