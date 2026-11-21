using Akira.Server.Models.Playback;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IWatchStateImportService
{
    /// <summary>
    /// Adds watch state from another server to the user's own, matched on TMDB, TVDB or IMDb ids; newer state here is kept.
    /// </summary>
    Task<ServiceResult<WatchStateImportResult>> ImportAsync(Guid userId, WatchStateImportRequest request, CancellationToken cancellationToken);
}
