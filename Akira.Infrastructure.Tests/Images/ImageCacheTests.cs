using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Images;
using Akira.Infrastructure.Integrations;
using Xunit;

namespace Akira.Infrastructure.Tests.Images;

public class ImageCacheTests
{
    [Fact]
    public async Task OverItsCap_TheLeastRecentlyUsedGoesFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var folder = Directory.CreateTempSubdirectory("akira_image_cache_").FullName;
        try
        {
            using var cache = new ImageCache(Options.Create(new ImageCacheOptions { Path = folder, MaxMegabytes = 1 }),
                TimeProvider.System, NullLogger<ImageCache>.Instance);
            var poster = new ImageData(new byte[400 * 1024], "image/jpeg");

            await cache.StoreAsync("a", poster, cancellationToken);
            await cache.StoreAsync("b", poster, cancellationToken);
            Assert.NotNull(await cache.GetAsync("a", cancellationToken));
            await cache.StoreAsync("c", poster with { ContentType = "image/png" }, cancellationToken);

            Assert.Equal(poster.Content, (await cache.GetAsync("a", cancellationToken))?.Content);
            Assert.Null(await cache.GetAsync("b", cancellationToken));
            Assert.Equal("image/png", (await cache.GetAsync("c", cancellationToken))?.ContentType);
            Assert.Equal(2, Directory.GetFiles(folder).Length);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
