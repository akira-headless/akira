using Akira.Server.Models.Files;
using Akira.Server.Models.Updates;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IClientUpdateService
{
    /// <summary>
    /// Retrieves the newest build of a platform's app, or null when there is no source for it or nothing came yet.
    /// </summary>
    ClientUpdateModel? Get(string platform);

    /// <summary>
    /// Asks a platform's source for a newer build and downloads it, then returns the newest build the server holds.
    /// </summary>
    Task<ServiceResult<ClientUpdateModel>> CheckAsync(string platform, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the file of that build on disk to send.
    /// </summary>
    FileDelivery Deliver(string platform);
}
