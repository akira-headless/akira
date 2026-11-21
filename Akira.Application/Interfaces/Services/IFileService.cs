using Akira.Server.Models.Files;

namespace Akira.Server.Interfaces.Services;

public interface IFileService
{
    /// <summary>
    /// Retrieves a version with its tracks and chapters, or null when it does not exist.
    /// </summary>
    Task<FileDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a version on disk to stream, with its type and the validators a resumed download is checked against.
    /// </summary>
    Task<FileDelivery> DeliverVideoAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds an external subtitle file of a version on disk.
    /// </summary>
    Task<FileDelivery> DeliverSubtitleAsync(Guid id, string streamId, CancellationToken cancellationToken);

    /// <summary>
    /// Converts a text subtitle of a version, embedded or external, to WebVTT for browsers; Failed when it could not be
    /// converted.
    /// </summary>
    Task<FileDelivery> DeliverWebVttAsync(Guid id, string streamId, CancellationToken cancellationToken);
}
