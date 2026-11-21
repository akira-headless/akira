using System.Security.Claims;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IDeviceService
{
    /// <summary>
    /// Lists every device for an admin, and a user's own personal devices otherwise.
    /// </summary>
    Task<IReadOnlyList<DeviceSummary>> ListAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a device and every token it holds.
    /// </summary>
    Task<ServiceResult<bool>> RevokeAsync(ClaimsPrincipal principal, Guid id, CancellationToken cancellationToken);
}
