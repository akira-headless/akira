using Akira.Core.Enum;
using Akira.Infrastructure.Integrations;

namespace Akira.Server.Interfaces.Services;

public interface IImageService
{
    /// <summary>
    /// Retrieves an item's image through Radarr or Sonarr, falling back to the artwork host, in a smaller size when one exists.
    /// Returns null when the item has no such image, throws UpstreamException when it could not be fetched.
    /// </summary>
    Task<ImageData?> GetAsync(Guid itemId, ImageKind kind, ImageSize size, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the tag a client can hold an item's image by: it changes when the picture it would get does, and takes
    /// no call to Radarr, Sonarr or the artwork host. Returns null when the item has no such image.
    /// </summary>
    Task<string?> GetTagAsync(Guid itemId, ImageKind kind, ImageSize size, CancellationToken cancellationToken);
}
