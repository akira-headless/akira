using System.Security.Claims;
using Akira.Server.Models.Actions;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IRemovalSettingsService
{
    /// <summary>
    /// Retrieves what removing does by default and whether users other than admins may remove.
    /// </summary>
    Task<ServerRemovalSettings> GetServerAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets what removing does by default and whether users other than admins may remove.
    /// </summary>
    Task<ServiceResult<ServerRemovalSettings>> SetServerAsync(ServerRemovalSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a user's override, what applies to them and whether they may remove at all.
    /// </summary>
    Task<UserRemovalSettings> GetForUserAsync(ClaimsPrincipal user, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a user's override. A type left empty follows the server default.
    /// </summary>
    Task<UserRemovalSettings> SetForUserAsync(ClaimsPrincipal user, RemovalBehaviors behaviors, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a user may delete or unmonitor: admins always, others when the server allows it.
    /// </summary>
    Task<bool> MayRemoveAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}
