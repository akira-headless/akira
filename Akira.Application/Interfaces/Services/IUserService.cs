using System.Security.Claims;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IUserService
{
    Task<ServiceResult<CurrentUser>> GetCurrentAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken);

    Task<ServiceResult<UserSummary>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<UserSummary>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a user with their devices, sessions and API keys. The last admin cannot be deleted.
    /// </summary>
    Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Sets or removes a PIN. A user changing their own needs the current one, an admin resetting someone else's does not.
    /// </summary>
    Task<ServiceResult<UserSummary>> SetPinAsync(ClaimsPrincipal principal, Guid userId, SetPinRequest request, CancellationToken cancellationToken);
}
