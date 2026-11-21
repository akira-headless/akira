using Microsoft.EntityFrameworkCore;
using Akira.Core.Enum;
using Akira.Core.Library;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Images;
using Akira.Infrastructure.Integrations;
using Akira.Server.Interfaces.Services;

namespace Akira.Server.Services;

public class ImageHashService : IImageHashService
{
    private const int Batch = 20;

    // One image that cannot be fetched is that image. This many in a row is its source being away.
    private const int FailuresInARow = 3;

    private readonly AkiraDbContext _dbContext;
    private readonly IImageService _images;
    private readonly IImageDecoder _decoder;
    private readonly ILogger<ImageHashService> _logger;

    public ImageHashService(
        AkiraDbContext dbContext,
        IImageService images,
        IImageDecoder decoder,
        ILogger<ImageHashService> logger)
    {
        _dbContext = dbContext;
        _images = images;
        _decoder = decoder;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ImageHashBatch> HashNextAsync(int skip, CancellationToken cancellationToken)
    {
        // A logo is cut out on a transparent background, a blur of it shows nothing worth waiting on
        var next = await _dbContext.MediaImages.AsNoTracking()
            .Where(image => image.BlurHash == null && image.Kind != ImageKind.Logo)
            .Select(image => new { image.MediaItemId, image.Kind })
            .Distinct()
            .OrderByDescending(image => image.MediaItemId)
            .ThenBy(image => image.Kind)
            .Skip(skip)
            .Take(Batch)
            .ToListAsync(cancellationToken);

        var left = 0;
        var inARow = 0;
        foreach (var image in next)
        {
            string hash;
            try
            {
                // The small copy has all a blur needs and is a fraction of the original
                var data = await _images.GetAsync(image.MediaItemId, image.Kind, ImageSize.Small, cancellationToken);
                if (data is null)
                {
                    hash = string.Empty;
                }
                else
                {
                    var pixels = await _decoder.DecodeAsync(data.Content, cancellationToken);
                    hash = Hash(pixels, image.Kind);
                }
            }
            catch (UpstreamException exception) when (exception.Failure == UpstreamFailure.NotFound)
            {
                hash = string.Empty;
            }
            catch (ImageDecodeException exception) when (exception.Failure == ImageDecodeFailure.Failed)
            {
                _logger.LogInformation("The {Kind} of item {ItemId} has no blur: {Reason}", image.Kind, image.MediaItemId, exception.Message);
                hash = string.Empty;
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not ImageDecodeException)
            {
                // It keeps its place without a blur and the caller skips it until the next round. Left to end the
                // batch, the same image would be first again every time and none after it would get a blur.
                _logger.LogInformation("The {Kind} of item {ItemId} has no blur yet: {Reason}", image.Kind, image.MediaItemId, exception.Message);
                left++;
                if (++inARow == FailuresInARow)
                {
                    return new ImageHashBatch(next.Count, left, true, (exception as UpstreamException)?.RetryAfter);
                }

                continue;
            }

            inARow = 0;

            // Every instance's copy of the picture gets the same blur
            await _dbContext.MediaImages
                .Where(row => row.MediaItemId == image.MediaItemId && row.Kind == image.Kind)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.BlurHash, hash), cancellationToken);
        }

        return new ImageHashBatch(next.Count, left, false, null);
    }

    // The pixels come square; a poster is tall and gets more components down, the rest are wide
    private static string Hash(byte[] pixels, ImageKind kind) => kind == ImageKind.Poster
        ? BlurHash.Encode(pixels, ImageDecoder.Size, ImageDecoder.Size, 3, 4)
        : BlurHash.Encode(pixels, ImageDecoder.Size, ImageDecoder.Size, 4, 3);
}
