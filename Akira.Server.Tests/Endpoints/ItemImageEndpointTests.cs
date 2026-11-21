using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Interfaces.Integrations;
using Akira.Server.Testing;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Endpoints;

public class ItemImageEndpointTests
{
    private static readonly ImageData Jpeg = new([0xFF, 0xD8, 0xFF], "image/jpeg");

    [Fact]
    public async Task Poster_ComesFromTheFirstConfiguredInstance_WithCacheHeader()
    {
        var radarr = new Mock<IRadarrClient>();
        radarr.Setup(client => client.GetMediaCoverAsync(949, "poster.jpg", It.IsAny<CancellationToken>())).ReturnsAsync(Jpeg);
        var clients = Clients(radarr: radarr);

        var response = await GetImageAsync(clients, new Mock<IArtworkClient>(MockBehavior.Strict),
            ids => $"/api/v1/items/{ids.Heat}/images/poster");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((true, false, TimeSpan.FromDays(1)), (response.Headers.CacheControl!.Private, response.Headers.CacheControl.Public, response.Headers.CacheControl.MaxAge));
        Assert.Equal(Jpeg.Content, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(response.Headers.ETag);
        clients.Verify(factory => factory.CreateRadarr(It.Is<ArrInstanceOptions>(i => i.Key == "radarr")), Times.Once);
    }

    [Fact]
    public async Task Poster_AskedAgainWithItsETag_IsNotModified_WithoutAskingRadarr()
    {
        var radarr = new Mock<IRadarrClient>();
        radarr.Setup(client => client.GetMediaCoverAsync(949, "poster.jpg", It.IsAny<CancellationToken>())).ReturnsAsync(Jpeg);

        var response = await GetImageAsync(Clients(radarr), new Mock<IArtworkClient>(MockBehavior.Strict),
            ids => $"/api/v1/items/{ids.Heat}/images/poster", revalidate: true);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal(TimeSpan.FromDays(1), response.Headers.CacheControl!.MaxAge);
        radarr.Verify(client => client.GetMediaCoverAsync(949, "poster.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Poster_AskedByAnotherClient_ComesFromTheDiskCache_WithoutAskingRadarr()
    {
        var radarr = new Mock<IRadarrClient>();
        radarr.Setup(client => client.GetMediaCoverAsync(949, "poster.jpg", It.IsAny<CancellationToken>())).ReturnsAsync(Jpeg);

        var response = await GetImageAsync(Clients(radarr), new Mock<IArtworkClient>(MockBehavior.Strict),
            ids => $"/api/v1/items/{ids.Heat}/images/poster", askAgain: true);

        Assert.Equal((HttpStatusCode.OK, "image/jpeg"), (response.StatusCode, response.Content.Headers.ContentType?.MediaType));
        Assert.Equal(Jpeg.Content, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        radarr.Verify(client => client.GetMediaCoverAsync(949, "poster.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AKodiLibrary_GetsArtworkThroughALink_WithoutItsToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var radarr = new Mock<IRadarrClient>();
        radarr.Setup(client => client.GetMediaCoverAsync(949, "poster.jpg", It.IsAny<CancellationToken>())).ReturnsAsync(Jpeg);
        var clients = Clients(radarr: radarr);
        await using var configured = new AkiraApiFactory { Instances = LibrarySeed.Instances, TestServices = services => services.AddSingleton(clients.Object) };
        LibrarySeed.Ids ids;
        using (var scope = configured.Services.CreateScope())
        {
            ids = await LibrarySeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AkiraDbContext>(), cancellationToken);
        }

        using var admin = configured.CreateClient();
        using var anonymous = configured.CreateAnonymousClient();
        var token = await PairAsync(admin, anonymous, "personal");
        using var kodi = configured.CreateClient(token);

        var (status, body, _) = await PostAsync(kodi, "/api/v1/auth/artwork-link", new { });
        var template = body.GetProperty("urlTemplate").GetString()!;
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain(token, template);

        var poster = await anonymous.GetAsync(template.Replace("{itemId}", ids.Heat.ToString()).Replace("{kind}", "poster"), cancellationToken);
        Assert.Equal(Jpeg.Content, await poster.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.GetAsync(template.Replace("{itemId}", ids.Heat.ToString()).Replace("{kind}", "files"), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Poster_FallsBackToTheArtworkHost_ThenTheNextInstance()
    {
        var unreachable = new UpstreamException("Radarr", UpstreamFailure.Unreachable, "Radarr could not be reached.");
        var radarr = new Mock<IRadarrClient>();
        radarr.Setup(client => client.GetMediaCoverAsync(949, "poster.jpg", It.IsAny<CancellationToken>())).ThrowsAsync(unreachable);
        var radarr4K = new Mock<IRadarrClient>();
        radarr4K.Setup(client => client.GetMediaCoverAsync(11, "poster.jpg", It.IsAny<CancellationToken>())).ReturnsAsync(Jpeg);
        var artwork = new Mock<IArtworkClient>();
        artwork.Setup(client => client.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UpstreamException("Artwork host", UpstreamFailure.Timeout, "timeout"));

        var response = await GetImageAsync(Clients(radarr, radarr4K), artwork, ids => $"/api/v1/items/{ids.Heat}/images/poster");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        artwork.Verify(client => client.GetAsync("https://image.tmdb.org/t/p/original/heat.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MediumPoster_AsksForTheResizedCopy_ThenTheOriginalWhenThereIsNone()
    {
        var radarr = new Mock<IRadarrClient>();
        radarr.Setup(client => client.GetMediaCoverAsync(949, "poster-500.jpg", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UpstreamException("Radarr", UpstreamFailure.NotFound, "Not resized."));
        radarr.Setup(client => client.GetMediaCoverAsync(949, "poster.jpg", It.IsAny<CancellationToken>())).ReturnsAsync(Jpeg);

        var response = await GetImageAsync(Clients(radarr), new Mock<IArtworkClient>(MockBehavior.Strict),
            ids => $"/api/v1/items/{ids.Heat}/images/poster?size=medium");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        radarr.Verify(client => client.GetMediaCoverAsync(949, "poster-500.jpg", It.IsAny<CancellationToken>()), Times.Once);
        radarr.Verify(client => client.GetMediaCoverAsync(949, "poster.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(UpstreamFailure.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData(UpstreamFailure.Unauthorized, HttpStatusCode.BadGateway)]
    [InlineData(UpstreamFailure.Malformed, HttpStatusCode.BadGateway)]
    [InlineData(UpstreamFailure.NotFound, HttpStatusCode.NotFound)]
    public async Task Still_UpstreamFailure_IsMappedToStatus(UpstreamFailure failure, HttpStatusCode expected)
    {
        var artwork = new Mock<IArtworkClient>();
        artwork.Setup(client => client.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UpstreamException("Artwork host", failure, "failed"));

        var response = await GetImageAsync(Clients(), artwork, ids => $"/api/v1/items/{ids.Episode1}/images/still");

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task MissingKind_IsNotFound()
    {
        var response = await GetImageAsync(Clients(), new Mock<IArtworkClient>(), ids => $"/api/v1/items/{ids.Heat}/images/banner");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownKind_IsBadRequest()
    {
        var response = await GetImageAsync(Clients(), new Mock<IArtworkClient>(), ids => $"/api/v1/items/{ids.Heat}/images/thumbnail");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Mock<IArrClientFactory> Clients(Mock<IRadarrClient>? radarr = null, Mock<IRadarrClient>? radarr4K = null)
    {
        var clients = new Mock<IArrClientFactory>();
        clients.Setup(factory => factory.CreateRadarr(It.Is<ArrInstanceOptions>(i => i.Key == "radarr")))
            .Returns((radarr ?? new Mock<IRadarrClient>()).Object);
        clients.Setup(factory => factory.CreateRadarr(It.Is<ArrInstanceOptions>(i => i.Key == "radarr-4k")))
            .Returns((radarr4K ?? new Mock<IRadarrClient>()).Object);
        return clients;
    }

    private static async Task<HttpResponseMessage> GetImageAsync(
        Mock<IArrClientFactory> clients,
        Mock<IArtworkClient> artwork,
        Func<LibrarySeed.Ids, string> url,
        bool revalidate = false,
        bool askAgain = false)
    {
        await using var factory = new AkiraApiFactory { Instances = LibrarySeed.Instances };
        var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(clients.Object);
            services.AddScoped(_ => artwork.Object);
        }));

        LibrarySeed.Ids ids;
        using (var scope = configured.Services.CreateScope())
        {
            ids = await LibrarySeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AkiraDbContext>(),
                TestContext.Current.CancellationToken);
        }

        using var client = configured.CreateClient();
        var response = await client.GetAsync(url(ids), TestContext.Current.CancellationToken);
        await response.Content.LoadIntoBufferAsync(TestContext.Current.CancellationToken);
        if (askAgain)
        {
            var second = await client.GetAsync(url(ids), TestContext.Current.CancellationToken);
            await second.Content.LoadIntoBufferAsync(TestContext.Current.CancellationToken);
            return second;
        }

        if (!revalidate)
        {
            return response;
        }

        // What a browser does with an answer older than its max-age
        using var again = new HttpRequestMessage(HttpMethod.Get, url(ids));
        again.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        var revalidated = await client.SendAsync(again, TestContext.Current.CancellationToken);
        await revalidated.Content.LoadIntoBufferAsync(TestContext.Current.CancellationToken);
        return revalidated;
    }
}
