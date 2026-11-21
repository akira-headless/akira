using System.Net;
using System.Text;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Images;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Integrations;

public class ArtworkClientTests
{
    [Fact]
    public async Task Get_FetchesFromAllowedHost()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(StubHttpHandler.Image([9], "image/png")));
        const string url = "https://artworks.thetvdb.com/banners/v4/series/1/clearlogo/a.png";

        var image = await new ArtworkClient(new HttpClient(handler)).GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(url, Assert.Single(handler.Requests).RequestUri!.ToString());
        Assert.Equal("image/png", image.ContentType);
    }

    [Fact]
    public async Task Get_ForbiddenImage_IsNotFound()
    {
        // TheTVDB answers a removed image with 403 and an S3 error document
        var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<Error><Code>AccessDenied</Code></Error>", Encoding.UTF8, "application/xml")
        }));
        const string url = "https://artworks.thetvdb.com/banners/v4/episode/1/screencap/gone.jpg";

        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => new ArtworkClient(new HttpClient(handler)).GetAsync(url, TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.NotFound, exception.Failure);
    }

    [Theory]
    [InlineData("https://evil.example/a.jpg")]
    [InlineData("http://image.tmdb.org/a.jpg")]
    [InlineData("http://192.168.1.1/admin")]
    [InlineData("not a url")]
    public async Task Get_RefusesOtherUrlsWithoutARequest(string url)
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(StubHttpHandler.Image([9], "image/png")));

        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => new ArtworkClient(new HttpClient(handler)).GetAsync(url, TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.NotFound, exception.Failure);
        Assert.Empty(handler.Requests);
    }
}
