using System.Security.Claims;
using Akira.Server.Models.Actions;
using Akira.Server.Models.Items;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IActionService
{
    /// <summary>
    /// Deletes the files of a movie, episode, season or series in the named instances and unmonitors it, or plans that on a dry run.
    /// </summary>
    Task<ServiceResult<ActionResult>> DeleteAsync(ClaimsPrincipal user, Guid itemId, DeleteActionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Unmonitors a movie, episode, season or series in the named instances and keeps its files.
    /// </summary>
    Task<ServiceResult<ActionResult>> UnmonitorAsync(ClaimsPrincipal user, Guid itemId, UnmonitorActionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Marks an item played or unplayed for the user, with every episode below a season or series.
    /// </summary>
    Task<ServiceResult<MarkPlayedResult>> MarkPlayedAsync(ClaimsPrincipal user, Guid itemId, MarkPlayedRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the audit log, newest first.
    /// </summary>
    Task<PagedResponse<AuditEntryModel>> ListAuditAsync(int offset, int limit, CancellationToken cancellationToken);
}
