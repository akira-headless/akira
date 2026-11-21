using Akira.Core.Enum;
using Akira.Server.Models.Items;

namespace Akira.Server.Interfaces.Services;

public interface IImageDeliveryService
{
    /// <summary>
    /// Finds what to answer a request for an item's image with. A client that holds the current tag (heldTags, from
    /// If-None-Match) gets NotModified without a call to Radarr, Sonarr or the artwork host.
    /// </summary>
    Task<ImageDelivery> DeliverAsync(Guid itemId, ImageKind kind, ImageSize size, IReadOnlyCollection<string> heldTags,
        CancellationToken cancellationToken);
}
