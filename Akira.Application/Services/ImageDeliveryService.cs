using Akira.Core.Enum;
using Akira.Infrastructure.Integrations;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Files;
using Akira.Server.Models.Items;

namespace Akira.Server.Services;

public sealed class ImageDeliveryService : IImageDeliveryService
{
    private readonly IImageService _images;

    public ImageDeliveryService(IImageService images)
    {
        _images = images;
    }

    /// <inheritdoc />
    public async Task<ImageDelivery> DeliverAsync(Guid itemId, ImageKind kind, ImageSize size, IReadOnlyCollection<string> heldTags,
        CancellationToken cancellationToken)
    {
        if (await _images.GetTagAsync(itemId, kind, size, cancellationToken) is not { } tag)
        {
            return new ImageDelivery(DeliveryStatus.NotFound);
        }

        // A client that asks again after a day gets its answer from the database
        if (heldTags.Contains(tag))
        {
            return new ImageDelivery(DeliveryStatus.NotModified, tag);
        }

        try
        {
            return await _images.GetAsync(itemId, kind, size, cancellationToken) is { } image
                ? new ImageDelivery(DeliveryStatus.Ready, tag, image)
                : new ImageDelivery(DeliveryStatus.NotFound);
        }
        catch (UpstreamException exception)
        {
            return new ImageDelivery(DeliveryStatus.Failed, tag) { Failure = exception.Failure, Detail = exception.Message };
        }
    }
}
