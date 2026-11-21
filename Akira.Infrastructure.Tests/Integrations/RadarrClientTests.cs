using System.Net;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Radarr;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Integrations;

public class RadarrClientTests
{
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task GetMovies_ReadsRealResponse()
    {
        var handler = StubHttpHandler.Json(Fixtures.Read("radarr-movies.json"));

        var movies = await CreateClient(handler).GetMoviesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, movies.Count);
        var movie = movies.Single(m => m.TmdbId == 170);
        Assert.Equal("28 Days Later", movie.Title);
        Assert.Equal("English", movie.OriginalLanguage?.Name);
        Assert.True(movie.HasFile);
        Assert.Equal(2, movie.MovieFile?.Id);
        Assert.Equal("/movies/28 Days Later (2002)/28 Days Later 2002 Bluray-1080p.mp4", movie.MovieFile?.Path);
        Assert.Contains(movie.Images!, image => image.CoverType == "poster" && image.RemoteUrl!.StartsWith("https://image.tmdb.org"));
    }

    [Fact]
    public async Task GetMovies_SendsKeyAsHeaderAndKeepsUrlBase()
    {
        var handler = StubHttpHandler.Json("[]");

        await CreateClient(handler, "http://nas:7878/radarr").GetMoviesAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://nas:7878/radarr/api/v3/movie", request.RequestUri!.ToString());
        Assert.Equal(ApiKey, Assert.Single(request.Headers.GetValues("X-Api-Key")));
        Assert.DoesNotContain(ApiKey, request.RequestUri.ToString());
    }

    [Fact]
    public async Task GetMovies_EmptyLibrary_IsAnEmptyListNotAFailure()
    {
        var movies = await CreateClient(StubHttpHandler.Json("[]")).GetMoviesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(movies);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, UpstreamFailure.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, UpstreamFailure.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, UpstreamFailure.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, UpstreamFailure.RateLimited)]
    [InlineData(HttpStatusCode.Found, UpstreamFailure.UnexpectedStatus)]
    [InlineData(HttpStatusCode.InternalServerError, UpstreamFailure.UnexpectedStatus)]
    public async Task GetMovies_ErrorStatus_IsClassified(HttpStatusCode status, UpstreamFailure expected)
    {
        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => CreateClient(StubHttpHandler.Status(status)).GetMoviesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(expected, exception.Failure);
        Assert.Equal("Radarr", exception.Upstream);
        Assert.DoesNotContain(ApiKey, exception.Message);
    }

    [Fact]
    public async Task GetMovies_RateLimited_CarriesRetryAfter()
    {
        var handler = new StubHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        });

        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => CreateClient(handler).GetMoviesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task GetMovies_LoginPage_IsMalformedNotEmpty()
    {
        var handler = new StubHttpHandler((_, _) =>
            Task.FromResult(StubHttpHandler.Response(HttpStatusCode.OK, "<html>login</html>", "text/html")));

        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => CreateClient(handler).GetMoviesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.Malformed, exception.Failure);
        Assert.Contains("text/html", exception.Message);
    }

    [Theory]
    [InlineData("{\"not\": \"a list\"}")]
    [InlineData("[{\"id\": \"one\"")]
    [InlineData("null")]
    public async Task GetMovies_UnreadableJson_IsMalformed(string json)
    {
        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => CreateClient(StubHttpHandler.Json(json)).GetMoviesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.Malformed, exception.Failure);
    }

    [Fact]
    public async Task GetMovies_ConnectionRefused_IsUnreachable()
    {
        var handler = StubHttpHandler.Throws(new HttpRequestException("Connection refused (nas:7878)"));

        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => CreateClient(handler).GetMoviesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.Unreachable, exception.Failure);
    }

    [Fact]
    public async Task GetMovies_NoAnswerInTime_IsTimeout()
    {
        var handler = new StubHttpHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = CreateClient(handler, timeout: TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => client.GetMoviesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.Timeout, exception.Failure);
    }

    [Fact]
    public async Task GetMediaCover_UsesApiRoute()
    {
        var handler = new StubHttpHandler((_, _) => Task.FromResult(StubHttpHandler.Image([1, 2, 3], "image/jpeg")));

        var image = await CreateClient(handler).GetMediaCoverAsync(1, "poster.jpg", TestContext.Current.CancellationToken);

        Assert.Equal("http://radarr:7878/api/v3/mediacover/1/poster.jpg", Assert.Single(handler.Requests).RequestUri!.ToString());
        Assert.Equal([1, 2, 3], image.Content);
        Assert.Equal("image/jpeg", image.ContentType);
    }

    [Fact]
    public async Task GetMediaCover_NonImageContentType_IsMalformed()
    {
        var handler = new StubHttpHandler((_, _) =>
            Task.FromResult(StubHttpHandler.Response(HttpStatusCode.OK, "<svg onload=alert(1)>", "image/svg+xml")));

        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => CreateClient(handler).GetMediaCoverAsync(1, "poster.jpg", TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.Malformed, exception.Failure);
    }

    [Fact]
    public async Task GetMediaCover_TooLarge_IsRefusedBeforeReading()
    {
        var handler = new StubHttpHandler((_, _) =>
        {
            var response = StubHttpHandler.Image([1], "image/jpeg");
            response.Content.Headers.ContentLength = UpstreamHttp.MaxImageBytes + 1;
            return Task.FromResult(response);
        });

        var exception = await Assert.ThrowsAsync<UpstreamException>(
            () => CreateClient(handler).GetMediaCoverAsync(1, "poster.jpg", TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.Malformed, exception.Failure);
    }

    [Theory]
    [InlineData("../config.xml")]
    [InlineData("poster.jpg/../../x")]
    public async Task GetMediaCover_RejectsFileNamesThatAreNotMediaCovers(string fileName)
    {
        var handler = StubHttpHandler.Json("[]");

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateClient(handler).GetMediaCoverAsync(1, fileName, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ChangeCalls_SendWhatRadarrExpects()
    {
        var handler = StubHttpHandler.Status(HttpStatusCode.Accepted);
        var client = CreateClient(handler);

        await client.DeleteMovieFileAsync(2, TestContext.Current.CancellationToken);
        await client.SetMonitoredAsync([1], false, TestContext.Current.CancellationToken);
        await client.SearchAsync([1], TestContext.Current.CancellationToken);

        Assert.Equal(
        [
            "DELETE http://radarr:7878/api/v3/moviefile/2 ",
            "PUT http://radarr:7878/api/v3/movie/editor {\"movieIds\":[1],\"monitored\":false}",
            "POST http://radarr:7878/api/v3/command {\"name\":\"MoviesSearch\",\"movieIds\":[1]}"
        ], handler.Requests.Select((r, i) => $"{r.Method} {r.RequestUri} {handler.Bodies[i]}"));
        Assert.All(handler.Requests, request => Assert.Equal(ApiKey, request.Headers.GetValues("X-Api-Key").Single()));
    }

    [Fact]
    public async Task ChangeCall_ThatRadarrRefuses_IsAnUpstreamFailure()
    {
        var exception = await Assert.ThrowsAsync<UpstreamException>(() =>
            CreateClient(StubHttpHandler.Status(HttpStatusCode.Unauthorized)).SetMonitoredAsync([1], false, TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.Unauthorized, exception.Failure);
    }

    private static RadarrClient CreateClient(
        StubHttpHandler handler,
        string url = "http://radarr:7878",
        TimeSpan? timeout = null)
    {
        var instance = new ArrInstanceOptions { Key = "radarr", Kind = ArrKind.Radarr, Url = url, ApiKey = ApiKey };
        var httpClient = new HttpClient(handler);
        ArrHttpClientConfiguration.Configure(httpClient, instance);
        if (timeout is { } value)
        {
            httpClient.Timeout = value;
        }

        return new RadarrClient(httpClient, "Radarr");
    }
}
