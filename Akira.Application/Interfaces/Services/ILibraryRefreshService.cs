using Akira.Server.Webhooks;

namespace Akira.Server.Interfaces.Services;

public interface ILibraryRefreshService
{
    /// <summary>
    /// Refreshes the movies and series webhooks named, publishes what changed and asks for a probe pass.
    /// </summary>
    Task RefreshAsync(IReadOnlyCollection<LibraryRefresh> refreshes, CancellationToken cancellationToken);
}
