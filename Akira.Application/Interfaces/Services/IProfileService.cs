using System.Security.Claims;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IProfileService
{
    /// <summary>
    /// Lists the profiles a device can use: everyone on a shared device, the owner on a personal one.
    /// </summary>
    Task<IReadOnlyList<ProfileSummary>> ListAsync(ClaimsPrincipal device, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a profile session on a shared device, checking the user's PIN when they have one.
    /// </summary>
    Task<ServiceResult<ProfileSession>> StartSessionAsync(ClaimsPrincipal device, Guid userId, string? pin, CancellationToken cancellationToken);
}
