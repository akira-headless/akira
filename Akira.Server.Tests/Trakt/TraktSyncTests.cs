using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Akira.Infrastructure.Configuration;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Trakt;
using Akira.Server.Services.Trakt;
using Akira.Server.Tests.Playback;
using Akira.Server.Testing;
using Akira.Server.Testing.Playback;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Trakt;

public abstract class TraktSyncTests
{
    protected abstract Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task WithoutAnApp_TheStatusSaysSo_AndLinkingIsRefused()
    {
        await using var api = await StartAsync(configured: false);

        var status = (await GetAsync(api.Admin, "/api/v1/users/me/trakt")).Body;

        Assert.False(status.GetProperty("configured").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(api.Admin, "/api/v1/users/me/trakt/link", new { })).Status);
    }

    [Fact]
    public async Task Linking_ShowsTheCode_WaitsForTheUser_AndImportsWhatTheyWatched()
    {
        await using var api = await StartAsync();

        var started = (await PostAsync(api.Admin, "/api/v1/users/me/trakt/link", new { })).Body;
        var waiting = (await PostAsync(api.Admin, "/api/v1/users/me/trakt/link/poll", new { })).Body;
        api.Trakt.Approve();
        var linked = (await PostAsync(api.Admin, "/api/v1/users/me/trakt/link/poll", new { })).Body;

        Assert.Equal(("ABCD1234", "https://trakt.tv/activate"), (started.GetProperty("pending").GetProperty("userCode").GetString(),
            started.GetProperty("pending").GetProperty("verificationUrl").GetString()));
        Assert.False(waiting.GetProperty("linked").GetBoolean());
        Assert.Equal((true, "rowan", JsonValueKind.Null), (linked.GetProperty("linked").GetBoolean(), linked.GetProperty("username").GetString(),
            linked.GetProperty("pending").ValueKind));
        Assert.DoesNotContain("access", (await GetAsync(api.Admin, "/api/v1/users/me/trakt")).Body.GetRawText(), StringComparison.OrdinalIgnoreCase);

        // Heat was watched twice on Trakt, an episode once; a movie the library does not have keeps its play for later
        var heat = (await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}")).Body.GetProperty("userState");
        Assert.Equal((true, 2), (heat.GetProperty("isPlayed").GetBoolean(), heat.GetProperty("playCount").GetInt32()));
        Assert.True((await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode2}")).Body.GetProperty("userState").GetProperty("isPlayed").GetBoolean());
        using var scope = api.Factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AkiraDbContext>().WatchStates
            .AnyAsync(w => w.IdentityKey == "movie:tmdb:603" && w.IsPlayed, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Playback_IsScrobbled_AndMarkingByHandGoesToTheHistory()
    {
        await using var api = await StartAsync();
        await LinkAsync(api);
        await SyncAsync(api);

        var session = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/playback-plan", new { })).Body.GetProperty("sessionId").GetGuid();
        await PostAsync(api.Admin, $"/api/v1/sessions/{session}/progress", new { positionMs = 0, isPaused = false });
        await PostAsync(api.Admin, $"/api/v1/sessions/{session}/stop", new { positionMs = PlaybackSeed.EpisodeDurationMs * 95 / 100 });
        await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/actions/mark-played", new { played = true });
        await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Alien}/actions/mark-played", new { played = true });
        await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Alien}/actions/mark-played", new { played = false });
        await SyncAsync(api);

        Assert.Equal(["scrobble/start", "scrobble/stop", "history/add", "history/remove"], api.Trakt.Calls.Select(call => call.Name));
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var stop = JsonSerializer.SerializeToElement(api.Trakt.Calls[1].Body, web);
        Assert.Equal((1, 1, 1, 95.0), (stop.GetProperty("show").GetProperty("ids").GetProperty("tvdb").GetInt32(),
            stop.GetProperty("episode").GetProperty("season").GetInt32(), stop.GetProperty("episode").GetProperty("number").GetInt32(),
            stop.GetProperty("progress").GetDouble()));
        // Alien ended unplayed, so it is only removed
        var added = JsonSerializer.SerializeToElement(api.Trakt.Calls[2].Body, web).GetProperty("movies");
        var removed = JsonSerializer.SerializeToElement(api.Trakt.Calls[3].Body, web).GetProperty("movies");
        Assert.Equal([949], added.EnumerateArray().Select(movie => movie.GetProperty("ids").GetProperty("tmdb").GetInt32()));
        Assert.Equal([348], removed.EnumerateArray().Select(movie => movie.GetProperty("ids").GetProperty("tmdb").GetInt32()));
        Assert.All(api.Trakt.Calls, call => Assert.Equal("access-1", call.Token));
    }

    [Fact]
    public async Task AMarkTraktCouldNotTake_GoesOutLater_AndThePlaybackIsNotScrobbledAgain()
    {
        await using var api = await StartAsync();
        await LinkAsync(api);
        await SyncAsync(api);

        api.Trakt.Away = true;
        await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/actions/mark-played", new { played = true });
        var session = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/playback-plan", new { })).Body.GetProperty("sessionId").GetGuid();
        await PostAsync(api.Admin, $"/api/v1/sessions/{session}/progress", new { positionMs = 0, isPaused = false });
        await SyncAsync(api);
        Assert.Equal(["scrobble/start"], api.Trakt.Calls.Select(call => call.Name));

        api.Trakt.Away = false;
        await SyncAsync(api);
        Assert.Equal(["scrobble/start", "history/add"], api.Trakt.Calls.Select(call => call.Name));
    }

    [Fact]
    public async Task AnExpiringToken_IsRefreshedFirst_AndUnlinkingRevokesIt()
    {
        await using var api = await StartAsync();
        api.Trakt.ExpiresIn = 600;
        // Linked by hand to keep every call: the import right after linking already needs a fresh token
        await PostAsync(api.Admin, "/api/v1/users/me/trakt/link", new { });
        api.Trakt.Approve();
        await PostAsync(api.Admin, "/api/v1/users/me/trakt/link/poll", new { });
        await SyncAsync(api);

        await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Alien}/actions/mark-played", new { played = true });
        await SyncAsync(api);
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(api.Admin, HttpMethod.Delete, "/api/v1/users/me/trakt"));

        Assert.Equal(["refresh", "history/add", "revoke"], api.Trakt.Calls.Select(call => call.Name).Where(name => name != "import"));
        Assert.Equal("access-2", api.Trakt.Calls.Single(call => call.Name == "history/add").Token);
        Assert.False((await GetAsync(api.Admin, "/api/v1/users/me/trakt")).Body.GetProperty("linked").GetBoolean());
    }

    private static async Task LinkAsync(TraktApi api)
    {
        await PostAsync(api.Admin, "/api/v1/users/me/trakt/link", new { });
        api.Trakt.Approve();
        await PostAsync(api.Admin, "/api/v1/users/me/trakt/link/poll", new { });
        api.Trakt.Calls.Clear();
    }

    private static async Task SyncAsync(TraktApi api)
    {
        using var scope = api.Factory.Services.CreateScope();
        while (await scope.ServiceProvider.GetRequiredService<ITraktSyncService>().SyncPendingAsync(TestContext.Current.CancellationToken))
        {
        }
    }

    private async Task<TraktApi> StartAsync(bool configured = true)
    {
        var (options, database) = await CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var mediaRoot = Directory.CreateTempSubdirectory("akira_trakt_").FullName;
        var trakt = new FakeTrakt { IsConfigured = configured };
        var factory = new AkiraApiFactory(options)
        {
            Settings = { ["Auth:KeysPath"] = Path.Combine(mediaRoot, "keys") },
            Instances =
            [
                new ArrInstanceOptions { Key = "radarr", Name = "Radarr", Kind = ArrKind.Radarr, Url = "http://radarr", ApiKey = "k" },
                new ArrInstanceOptions { Key = "radarr-4k", Name = "Radarr 4K", Kind = ArrKind.Radarr, Url = "http://radarr4k", ApiKey = "k" },
                new ArrInstanceOptions { Key = "sonarr", Name = "Sonarr", Kind = ArrKind.Sonarr, Url = "http://sonarr", ApiKey = "k" }
            ],
            TestServices = services => services.Replace(ServiceDescriptor.Singleton<ITraktClient>(trakt))
        };

        PlaybackSeed.Ids ids;
        using (var scope = factory.Services.CreateScope())
        {
            ids = await PlaybackSeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AkiraDbContext>(), mediaRoot, TestContext.Current.CancellationToken);
        }

        return new TraktApi(factory, factory.CreateClient(), trakt, ids, mediaRoot, database);
    }

    protected sealed record TraktApi(AkiraApiFactory Factory, HttpClient Admin, FakeTrakt Trakt, PlaybackSeed.Ids Ids, string MediaRoot, IAsyncDisposable? Database)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            await Factory.DisposeAsync();
            if (Database is not null)
            {
                await Database.DisposeAsync();
            }

            Directory.Delete(MediaRoot, recursive: true);
        }
    }

    public sealed class FakeTrakt : ITraktClient
    {
        private bool _approved;
        private int _tokens;

        public bool IsConfigured { get; set; } = true;
        public int ExpiresIn { get; set; } = 86_400;

        // Only the history calls: a scrobble is not sent again, so it shows that playback is not replayed
        public bool Away { get; set; }
        private static UpstreamException Unreachable => new("Trakt", UpstreamFailure.Unreachable, "Trakt could not be reached.");
        public List<(string Name, string? Token, object? Body)> Calls { get; } = [];

        public void Approve() => _approved = true;

        public Task<TraktDeviceCode> StartDeviceAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new TraktDeviceCode("device-1", "ABCD1234", "https://trakt.tv/activate", 600, 5));

        public Task<TraktPoll> PollDeviceAsync(string deviceCode, CancellationToken cancellationToken) =>
            Task.FromResult(_approved ? new TraktPoll(TraktPollStatus.Approved, NewToken()) : new TraktPoll(TraktPollStatus.Pending));

        public Task<TraktToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            Calls.Add(("refresh", refreshToken, null));
            ExpiresIn = 86_400;
            return Task.FromResult(NewToken());
        }

        public Task RevokeAsync(string accessToken, CancellationToken cancellationToken) => Record("revoke", accessToken, null);

        public Task<string> GetUsernameAsync(string accessToken, CancellationToken cancellationToken) => Task.FromResult("rowan");

        public Task ScrobbleAsync(string accessToken, string action, object body, CancellationToken cancellationToken) =>
            Record($"scrobble/{action}", accessToken, body);

        public Task AddHistoryAsync(string accessToken, object body, CancellationToken cancellationToken) =>
            Away ? Task.FromException(Unreachable) : Record("history/add", accessToken, body);

        public Task RemoveHistoryAsync(string accessToken, object body, CancellationToken cancellationToken) =>
            Away ? Task.FromException(Unreachable) : Record("history/remove", accessToken, body);

        public async Task<IReadOnlyList<TraktWatchedMovie>> GetWatchedMoviesAsync(string accessToken, CancellationToken cancellationToken)
        {
            await Record("import", accessToken, null);
            return
            [
                new TraktWatchedMovie(2, new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero), new TraktMedia(new TraktIdSet(949, null))),
                new TraktWatchedMovie(1, null, new TraktMedia(new TraktIdSet(603, null)))
            ];
        }

        public Task<IReadOnlyList<TraktWatchedShow>> GetWatchedShowsAsync(string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TraktWatchedShow>>(
            [
                new TraktWatchedShow(new TraktMedia(new TraktIdSet(null, 1)), [new TraktWatchedSeason(1, [new TraktWatchedEpisode(2, 1, null)])])
            ]);

        private TraktToken NewToken()
        {
            _tokens++;
            return new TraktToken($"access-{_tokens}", $"refresh-{_tokens}", ExpiresIn, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        private Task Record(string name, string? token, object? body)
        {
            Calls.Add((name, token, body));
            return Task.CompletedTask;
        }
    }
}
