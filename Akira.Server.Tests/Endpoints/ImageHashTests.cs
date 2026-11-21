using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Images;
using Akira.Infrastructure.Integrations;
using Akira.Server.Interfaces.Services;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests.Endpoints;

public class ImageHashTests
{
    [Fact]
    public async Task AnImageThatCannotBeFetched_KeepsItsPlace_AndTheOthersGetTheirBlur()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var images = new Mock<IImageService>();
        images.Setup(service => service.GetAsync(It.IsAny<Guid>(), ImageKind.Still, ImageSize.Small, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UpstreamException("Artwork host", UpstreamFailure.Unreachable, "The artwork host could not be reached."));
        images.Setup(service => service.GetAsync(It.IsAny<Guid>(), ImageKind.Poster, ImageSize.Small, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageData([0xFF, 0xD8, 0xFF], "image/jpeg"));
        var decoder = new Mock<IImageDecoder>();
        decoder.Setup(d => d.DecodeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[ImageDecoder.Size * ImageDecoder.Size * 3]);

        await using var factory = new AkiraApiFactory
        {
            Instances = LibrarySeed.Instances,
            TestServices = services =>
            {
                services.Replace(ServiceDescriptor.Scoped(_ => images.Object));
                services.Replace(ServiceDescriptor.Singleton(decoder.Object));
            }
        };
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
        var ids = await LibrarySeed.SeedAsync(dbContext, cancellationToken);
        var hashes = scope.ServiceProvider.GetRequiredService<IImageHashService>();

        // The still that cannot be fetched used to end the batch, and the same batch came back every round
        var batch = await hashes.HashNextAsync(0, cancellationToken);

        Assert.Equal(new ImageHashBatch(2, 1, false, null), batch);
        var rows = await dbContext.MediaImages.AsNoTracking().ToListAsync(cancellationToken);
        Assert.All(rows.Where(row => row.MediaItemId == ids.Heat), row => Assert.False(string.IsNullOrEmpty(row.BlurHash)));
        Assert.Null(rows.Single(row => row.MediaItemId == ids.Episode1).BlurHash);
        Assert.Equal(0, (await hashes.HashNextAsync(batch.Left, cancellationToken)).Taken);
    }
}
