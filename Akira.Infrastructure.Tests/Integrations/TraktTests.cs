using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Trakt;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Integrations;

public class TraktTests
{
    [Theory]
    [InlineData("movie:tmdb:949", 949, null, null, null)]
    [InlineData("series:tvdb:81189:s2:e13", null, 81189, 2, 13)]
    [InlineData("movie:radarr:12", null, null, null, null)]
    [InlineData("series:tvdb:81189:s2", null, null, null, null)]
    public void IdentityKeys_MapToTheIdsTraktKnows_OrToNothing(string key, int? tmdb, int? tvdb, int? season, int? episode)
    {
        var item = TraktItems.FromIdentityKey(key);

        Assert.Equal(tmdb is null && tvdb is null ? null : new TraktItem(tmdb, tvdb, season, episode), item);
    }

    [Fact]
    public void History_GroupsEpisodesUnderTheirShowAndSeason()
    {
        var at = new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);

        var json = Json(TraktItems.HistoryBody([
            (new TraktItem(949, null, null, null), at),
            (new TraktItem(null, 81189, 1, 1), at),
            (new TraktItem(null, 81189, 1, 2), at)
        ]));

        Assert.Equal(949, json.GetProperty("movies")[0].GetProperty("ids").GetProperty("tmdb").GetInt32());
        var show = json.GetProperty("shows").EnumerateArray().Single();
        Assert.Equal(81189, show.GetProperty("ids").GetProperty("tvdb").GetInt32());
        Assert.Equal([1, 2], show.GetProperty("seasons")[0].GetProperty("episodes").EnumerateArray().Select(e => e.GetProperty("number").GetInt32()));
        Assert.Equal("2026-11-01T20:00:00+00:00", show.GetProperty("seasons")[0].GetProperty("episodes")[0].GetProperty("watched_at").GetString());
        Assert.False(Json(TraktItems.RemoveBody([new TraktItem(949, null, null, null)])).GetProperty("movies")[0].TryGetProperty("watched_at", out _));
        Assert.Equal(42.5, Json(TraktItems.ScrobbleBody(new TraktItem(null, 81189, 1, 2), 42.5)).GetProperty("progress").GetDouble());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, TraktPollStatus.Pending)]
    [InlineData(HttpStatusCode.TooManyRequests, TraktPollStatus.SlowDown)]
    [InlineData(HttpStatusCode.Gone, TraktPollStatus.Expired)]
    [InlineData((HttpStatusCode)418, TraktPollStatus.Denied)]
    [InlineData(HttpStatusCode.Conflict, TraktPollStatus.Invalid)]
    public async Task ThePollStatus_IsReadFromTheStatusCode(HttpStatusCode status, TraktPollStatus expected)
    {
        var poll = await Client(StubHttpHandler.Status(status)).PollDeviceAsync("device", TestContext.Current.CancellationToken);

        Assert.Equal(expected, poll.Status);
    }

    [Fact]
    public async Task UserCalls_SendTheAppKeyVersionAndToken_AndReadTheWatchedShows()
    {
        // Shaped after the example in Trakt's API documentation for GET /sync/watched/shows, not a captured response
        var handler = StubHttpHandler.Json("""
            [{ "plays": 3, "last_watched_at": "2026-10-01T20:00:00.000Z", "show": { "title": "Breaking Bad", "ids": { "trakt": 1, "tvdb": 81189, "tmdb": 1396 } },
               "seasons": [{ "number": 1, "episodes": [{ "number": 1, "plays": 2, "last_watched_at": "2026-10-01T20:00:00.000Z" }] }] }]
            """);

        var shows = await Client(handler).GetWatchedShowsAsync("user-token", TestContext.Current.CancellationToken);

        Assert.Equal((81189, 1, 2), (shows[0].Show.Ids.Tvdb, shows[0].Seasons![0].Episodes[0].Number, shows[0].Seasons![0].Episodes[0].Plays));
        var request = handler.Requests.Single();
        Assert.Equal("https://api.trakt.tv/sync/watched/shows", request.RequestUri!.ToString());
        Assert.Equal(("2", "app-id", "Bearer user-token"), (request.Headers.GetValues("trakt-api-version").Single(),
            request.Headers.GetValues("trakt-api-key").Single(), request.Headers.Authorization!.ToString()));
    }

    [Fact]
    public async Task WithoutAnApp_NothingIsSent()
    {
        var handler = StubHttpHandler.Json("{}");
        var client = new TraktClient(new SingleClientFactory(handler), new FixedOptions(new TraktOptions()));

        Assert.False(client.IsConfigured);
        await Assert.ThrowsAsync<UpstreamException>(() => client.StartDeviceAsync(TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
        Assert.Equal(["Trakt"], new TraktOptionsValidator().Validate(null, new TraktOptions { ClientId = "only-half" }).Failures!.Select(f => f.Split(' ')[0]));
    }

    private static TraktClient Client(StubHttpHandler handler) =>
        new(new SingleClientFactory(handler), new FixedOptions(new TraktOptions { ClientId = "app-id", ClientSecret = "app-secret" }));

    private static JsonElement Json(object body) => JsonSerializer.SerializeToElement(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedOptions(TraktOptions options) : IOptionsMonitor<TraktOptions>
    {
        public TraktOptions CurrentValue => options;

        public TraktOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<TraktOptions, string?> listener) => null;
    }
}
