using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Akira.Infrastructure.Configuration;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Radarr;
using Akira.Infrastructure.Integrations.Sonarr;
using Akira.Infrastructure.Interfaces.Integrations;
using Akira.Infrastructure.Interfaces.Sync;
using Akira.Infrastructure.Sync;
using Akira.Server.Interfaces.Services;
using Akira.Server.Testing;
using Akira.Server.Webhooks;
using Akira.Testing;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Actions;

public abstract class ActionTests
{
    protected abstract Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task DeleteMovie_InOneInstance_UnmonitorsFirst_AndKeepsTheOtherVersion()
    {
        await using var api = await StartAsync();

        // A full sync is writing the library. The player that asked gets its answer anyway, the refresh waits its turn.
        var gate = api.Factory.Services.GetRequiredService<SyncLock>().Gate;
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Heat}/actions/delete",
                new { scope = "movie", deleteFiles = true, unmonitor = true, instances = new[] { "radarr" } })
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        gate.Release();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("succeeded", body.GetProperty("status").GetString());
        var outcome = Assert.Single(body.GetProperty("instances").EnumerateArray());
        Assert.Equal(("radarr", 1, 1000L, 1, 1), (outcome.GetProperty("instance").GetString(), outcome.GetProperty("files").GetInt32(),
            outcome.GetProperty("bytes").GetInt64(), outcome.GetProperty("filesDeleted").GetInt32(), outcome.GetProperty("unmonitored").GetInt32()));
        Assert.Equal(["monitored 1 false", "delete-file 5"], api.Radarr.Calls);
        Assert.Empty(api.Radarr4K.Calls);

        // The refresh worker runs what the action queued, and the 4K version is what is left
        await RefreshQueuedAsync(api);
        var versions = (await GetAsync(api.Admin, $"/api/v1/items/{api.Heat}")).Body.GetProperty("versions").EnumerateArray();
        Assert.Equal(["radarr-4k"], versions.Select(v => v.GetProperty("instance").GetString()));

        var audit = (await GetAsync(api.Admin, "/api/v1/audit")).Body.GetProperty("items")[0];
        Assert.Equal(("delete", "succeeded", "movie", "Heat", TestAdmin.Name),
            (audit.GetProperty("action").GetString(), audit.GetProperty("status").GetString(), audit.GetProperty("scope").GetString(),
                audit.GetProperty("title").GetString(), audit.GetProperty("userName").GetString()));
        Assert.Equal(["radarr"], audit.GetProperty("details").GetProperty("request").GetProperty("instances").EnumerateArray()
            .Select(i => i.GetString()));

        var types = EventTypes(await GetAsync(api.Admin, "/api/v1/events?since=0"));
        Assert.Equal(["action.completed", "library.item.updated"], types.Where(type => type is "action.completed" or "library.item.updated"));
    }

    [Fact]
    public async Task DryRun_SaysWhatWouldHappen_AndDoesNothing()
    {
        await using var api = await StartAsync();

        var (_, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Series}/actions/delete",
            new { scope = "series", deleteFiles = true, unmonitor = true, instances = new[] { "sonarr" }, dryRun = true });

        Assert.Equal(("planned", JsonValueKind.Null), (body.GetProperty("status").GetString(), body.GetProperty("auditId").ValueKind));
        var outcome = body.GetProperty("instances")[0];
        Assert.Equal((3, 3000L, 0), (outcome.GetProperty("files").GetInt32(), outcome.GetProperty("bytes").GetInt64(),
            outcome.GetProperty("filesDeleted").GetInt32()));
        Assert.Empty(api.Sonarr.Calls);
        Assert.Equal(0, (await GetAsync(api.Admin, "/api/v1/audit")).Body.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("{\"deleteFiles\":true,\"unmonitor\":true,\"instances\":[\"radarr\"]}")]
    [InlineData("{\"scope\":\"movie\",\"unmonitor\":true,\"instances\":[\"radarr\"]}")]
    [InlineData("{\"scope\":\"movie\",\"deleteFiles\":true,\"unmonitor\":true,\"instances\":[]}")]
    [InlineData("{\"scope\":\"movie\",\"deleteFiles\":false,\"unmonitor\":false,\"instances\":[\"radarr\"]}")]
    [InlineData("{\"scope\":\"series\",\"deleteFiles\":true,\"unmonitor\":true,\"instances\":[\"radarr\"]}")]
    [InlineData("{\"scope\":\"movie\",\"deleteFiles\":true,\"unmonitor\":true,\"instances\":[\"sonarr\"]}")]
    [InlineData("{\"scope\":\"movie\",\"deleteFiles\":true,\"unmonitor\":true,\"instances\":[\"nowhere\"]}")]
    [InlineData("{\"scope\":\"movie\",\"deleteFiles\":true,\"unmonitor\":true,\"search\":true,\"instances\":[\"radarr\"]}")]
    public async Task UnclearRequests_AreRefused_AndChangeNothing(string json)
    {
        await using var api = await StartAsync();

        var (status, _) = await PostJsonAsync(api.Admin, $"/api/v1/items/{api.Heat}/actions/delete", json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(api.Radarr.Calls);
    }

    [Fact]
    public async Task Users_MayOnlyRemove_OnceAnAdminAllowsIt()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        using var kid = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "personal", await CreateUserAsync(api.Admin, "Kid")));
        var request = new { scope = "movie", deleteFiles = false, unmonitor = true, instances = new[] { "radarr" } };

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(kid, $"/api/v1/items/{api.Heat}/actions/delete", request)).Status);
        Assert.False((await GetAsync(kid, "/api/v1/users/me/settings/removal")).Body.GetProperty("mayRemove").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, await SendAsync(kid, HttpMethod.Put, "/api/v1/settings/removal",
            new { defaults = Defaults("deleteAndUnmonitor"), usersMayRemove = true }));

        // A body without its defaults is refused, and so is a number where a behavior belongs
        Assert.Equal(HttpStatusCode.BadRequest, await SendAsync(api.Admin, HttpMethod.Put, "/api/v1/settings/removal", new { usersMayRemove = true }));
        Assert.Equal(HttpStatusCode.BadRequest, await SendAsync(api.Admin, HttpMethod.Put, "/api/v1/settings/removal",
            new { defaults = new { movie = 1, episode = 1, season = 1, series = 1 }, usersMayRemove = true }));

        Assert.Equal(HttpStatusCode.OK, await SendAsync(api.Admin, HttpMethod.Put, "/api/v1/settings/removal",
            new { defaults = Defaults("deleteAndUnmonitor"), usersMayRemove = true }));

        var (status, body, _) = await PostAsync(kid, $"/api/v1/items/{api.Heat}/actions/delete", request);
        Assert.Equal((HttpStatusCode.OK, "unmonitor"), (status, body.GetProperty("action").GetString()));
        Assert.Equal(["monitored 1 false"], api.Radarr.Calls);
    }

    [Fact]
    public async Task DeleteSeason_UnmonitorsTheSeasonAndItsEpisodes_ThenDeletesTheirFiles()
    {
        await using var api = await StartAsync();

        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Season1}/actions/delete",
            new { scope = "season", deleteFiles = true, unmonitor = true, instances = new[] { "sonarr" } });

        Assert.Equal((HttpStatusCode.OK, "succeeded"), (status, body.GetProperty("status").GetString()));
        Assert.Equal(["series 6 s1 false", "episodes 11,12,13 false", "delete-file 21", "delete-file 22"], api.Sonarr.Calls);
        await RefreshQueuedAsync(api);
        Assert.Equal(0, (await GetAsync(api.Admin, $"/api/v1/items/{api.Episode1}")).Body.GetProperty("versions").GetArrayLength());
        Assert.Equal("Mayfair Witches Season 1", (await GetAsync(api.Admin, "/api/v1/audit")).Body.GetProperty("items")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task DeleteKeepingMonitored_WithSearch_FetchesANewCopy()
    {
        await using var api = await StartAsync();

        await PostAsync(api.Admin, $"/api/v1/items/{api.Episode1}/actions/delete",
            new { scope = "episode", deleteFiles = true, unmonitor = false, search = true, instances = new[] { "sonarr" } });

        Assert.Equal(["delete-file 21", "search 11"], api.Sonarr.Calls);
        Assert.Equal("Mayfair Witches S01E01 Episode 1", (await GetAsync(api.Admin, "/api/v1/audit")).Body.GetProperty("items")[0]
            .GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnmonitorSeries_KeepsTheFiles()
    {
        await using var api = await StartAsync();

        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Series}/actions/unmonitor",
            new { scope = "series", instances = new[] { "sonarr" } });

        Assert.Equal((HttpStatusCode.OK, 4, 0), (status, body.GetProperty("instances")[0].GetProperty("unmonitored").GetInt32(),
            body.GetProperty("instances")[0].GetProperty("filesDeleted").GetInt32()));
        Assert.Equal(["series 6 false", "episodes 11,12,13,14 false"], api.Sonarr.Calls);
    }

    [Fact]
    public async Task FailureHalfway_IsReportedAsPartial_AndAudited()
    {
        await using var api = await StartAsync();
        api.Radarr.FailOn["delete-file"] = new UpstreamException("Radarr", UpstreamFailure.Unreachable, "Radarr could not be reached.");

        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Heat}/actions/delete",
            new { scope = "movie", deleteFiles = true, unmonitor = true, instances = new[] { "radarr", "radarr-4k" } });

        Assert.Equal((HttpStatusCode.OK, "partiallySucceeded"), (status, body.GetProperty("status").GetString()));
        var outcomes = body.GetProperty("instances").EnumerateArray().ToDictionary(o => o.GetProperty("instance").GetString()!);
        Assert.Equal(("partiallySucceeded", "Radarr could not be reached."),
            (outcomes["radarr"].GetProperty("status").GetString(), outcomes["radarr"].GetProperty("error").GetString()));
        Assert.Equal("succeeded", outcomes["radarr-4k"].GetProperty("status").GetString());
        Assert.Equal("partiallySucceeded", (await GetAsync(api.Admin, "/api/v1/audit")).Body.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task TheAuditEntry_IsWrittenBeforeRadarrHearsOfIt_AndASurpriseIsOneInstancesOutcome()
    {
        await using var api = await StartAsync();
        string? statusWhileDeleting = null;
        api.Radarr.BeforeCall = async call =>
        {
            if (call.StartsWith("delete-file", StringComparison.Ordinal))
            {
                var entry = (await GetAsync(api.Admin, "/api/v1/audit")).Body.GetProperty("items")[0];
                statusWhileDeleting = entry.GetProperty("status").GetString();
            }
        };
        api.Radarr.FailOn["delete-file"] = new InvalidOperationException("Something nobody expected.");

        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Heat}/actions/delete",
            new { scope = "movie", deleteFiles = true, unmonitor = true, instances = new[] { "radarr", "radarr-4k" } });

        // Should the server stop in the middle, the log already says the delete was started
        Assert.Equal("started", statusWhileDeleting);
        Assert.Equal((HttpStatusCode.OK, "partiallySucceeded"), (status, body.GetProperty("status").GetString()));
        var outcomes = body.GetProperty("instances").EnumerateArray().ToDictionary(o => o.GetProperty("instance").GetString()!);
        Assert.Equal(("partiallySucceeded", 1, "Something nobody expected."), (outcomes["radarr"].GetProperty("status").GetString(),
            outcomes["radarr"].GetProperty("unmonitored").GetInt32(), outcomes["radarr"].GetProperty("error").GetString()));
        Assert.Equal("succeeded", outcomes["radarr-4k"].GetProperty("status").GetString());

        var audit = (await GetAsync(api.Admin, "/api/v1/audit")).Body.GetProperty("items")[0];
        Assert.Equal(("partiallySucceeded", 2), (audit.GetProperty("status").GetString(),
            audit.GetProperty("details").GetProperty("instances").GetArrayLength()));
    }

    [Fact]
    public async Task EveryInstanceFailing_IsABadGateway_WithTheOutcome()
    {
        await using var api = await StartAsync();
        api.Radarr.FailOn["monitored"] = new UpstreamException("Radarr", UpstreamFailure.Unauthorized, "Radarr rejected the API key.");

        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Heat}/actions/delete",
            new { scope = "movie", deleteFiles = true, unmonitor = true, instances = new[] { "radarr" } });

        Assert.Equal((HttpStatusCode.BadGateway, "failed"), (status, body.GetProperty("status").GetString()));
        Assert.Empty(api.Radarr.Calls);
    }

    [Fact]
    public async Task AFileRadarrAlreadyDeleted_CountsAsDeleted()
    {
        await using var api = await StartAsync();
        api.Radarr.FailOn["delete-file"] = new UpstreamException("Radarr", UpstreamFailure.NotFound, "Radarr has nothing there.");

        var (_, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Heat}/actions/delete",
            new { scope = "movie", deleteFiles = true, unmonitor = false, instances = new[] { "radarr" } });

        // What was asked for is true, but this action did not delete it, and the audit log does not say it did
        var outcome = body.GetProperty("instances")[0];
        Assert.Equal(("succeeded", 0, 1), (body.GetProperty("status").GetString(), outcome.GetProperty("filesDeleted").GetInt32(),
            outcome.GetProperty("filesAlreadyGone").GetInt32()));
    }

    [Fact]
    public async Task MarkSeriesPlayed_MarksWhatCanBeWatched_AndUnplayedUndoesIt()
    {
        await using var api = await StartAsync();

        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Series}/actions/mark-played", new { played = true });
        Assert.Equal((HttpStatusCode.OK, 3), (status, body.GetProperty("items").GetInt32()));
        Assert.Equal(0, (await GetAsync(api.Admin, "/api/v1/items?type=series&unwatched=true")).Body.GetProperty("total").GetInt32());
        Assert.Equal(0, (await GetAsync(api.Admin, $"/api/v1/items/{api.Series}")).Body.GetProperty("unplayedCount").GetInt32());
        var episode = (await GetAsync(api.Admin, $"/api/v1/items/{api.Episode1}")).Body.GetProperty("userState");
        Assert.Equal((true, 1), (episode.GetProperty("isPlayed").GetBoolean(), episode.GetProperty("playCount").GetInt32()));

        // Marking it again does not count another play
        await PostAsync(api.Admin, $"/api/v1/items/{api.Episode1}/actions/mark-played", new { played = true });
        Assert.Equal(1, (await GetAsync(api.Admin, $"/api/v1/items/{api.Episode1}")).Body.GetProperty("userState").GetProperty("playCount").GetInt32());

        await PostAsync(api.Admin, $"/api/v1/items/{api.Series}/actions/mark-played", new { played = false });
        Assert.Equal(3, (await GetAsync(api.Admin, $"/api/v1/items/{api.Series}")).Body.GetProperty("unplayedCount").GetInt32());
        Assert.Equal(1, (await GetAsync(api.Admin, "/api/v1/items?type=series&unwatched=true")).Body.GetProperty("total").GetInt32());

        var events = EventTypes(await GetAsync(api.Admin, "/api/v1/events?since=0"));
        Assert.Equal(7, events.Count(type => type == "watchstate.changed"));
        Assert.Equal(["markPlayed", "markPlayed", "markUnplayed"], (await GetAsync(api.Admin, "/api/v1/audit")).Body.GetProperty("items")
            .EnumerateArray().Select(e => e.GetProperty("action").GetString()).Reverse());
    }

    [Fact]
    public async Task RemovalSettings_UserOverride_FallsBackToTheServerDefault()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        using var kid = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "personal", await CreateUserAsync(api.Admin, "Kid")));

        var initial = (await GetAsync(kid, "/api/v1/users/me/settings/removal")).Body;
        Assert.Equal("deleteAndUnmonitor", initial.GetProperty("effective").GetProperty("movie").GetString());
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("override").GetProperty("movie").ValueKind);

        await SendAsync(kid, HttpMethod.Put, "/api/v1/users/me/settings/removal", new { episode = "unmonitorOnly" });
        await SendAsync(api.Admin, HttpMethod.Put, "/api/v1/settings/removal", new
        {
            defaults = new { movie = "deleteKeepMonitored", episode = "deleteAndUnmonitor", season = "deleteAndUnmonitor", series = "unmonitorOnly" },
            usersMayRemove = false
        });

        var effective = (await GetAsync(kid, "/api/v1/users/me/settings/removal")).Body.GetProperty("effective");
        Assert.Equal(("deleteKeepMonitored", "unmonitorOnly", "unmonitorOnly"), (effective.GetProperty("movie").GetString(),
            effective.GetProperty("episode").GetString(), effective.GetProperty("series").GetString()));
        Assert.Equal("deleteKeepMonitored", (await GetAsync(api.Admin, "/api/v1/settings/removal")).Body.GetProperty("defaults")
            .GetProperty("movie").GetString());
    }

    [Fact]
    public async Task Audit_IsForAdmins_AndPaged()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        using var kid = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "personal", await CreateUserAsync(api.Admin, "Kid")));
        await PostAsync(kid, $"/api/v1/items/{api.Heat}/actions/mark-played", new { played = true });
        await PostAsync(api.Admin, $"/api/v1/items/{api.Heat}/actions/mark-played", new { played = true });

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(kid, "/api/v1/audit")).Status);
        var page = (await GetAsync(api.Admin, "/api/v1/audit?limit=1")).Body;
        Assert.Equal((2, 1, TestAdmin.Name), (page.GetProperty("total").GetInt32(), page.GetProperty("items").GetArrayLength(),
            page.GetProperty("items")[0].GetProperty("userName").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(api.Admin, "/api/v1/audit?limit=0")).Status);
    }

    // What the refresh worker does with what an action queued; the test host runs no workers
    private static async Task RefreshQueuedAsync(StartedApi api)
    {
        var queue = api.Factory.Services.GetRequiredService<LibraryRefreshQueue>();
        var batch = new List<LibraryRefresh>();
        while (queue.Reader.TryRead(out var refresh))
        {
            batch.Add(refresh);
        }

        Assert.NotEmpty(batch);
        Assert.True(queue.TakeUrgent());
        using var scope = api.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ILibraryRefreshService>().RefreshAsync(batch, TestContext.Current.CancellationToken);
    }

    private static object Defaults(string behavior) => new { movie = behavior, episode = behavior, season = behavior, series = behavior };

    private static List<string> EventTypes((HttpStatusCode Status, JsonElement Body) response) =>
        response.Body.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("type").GetString()!).ToList();

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostJsonAsync(HttpClient client, string url, string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(url, content, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<StartedApi> StartAsync()
    {
        var (options, database) = await CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var clients = new FakeArrClientFactory();
        var added = DateTimeOffset.UtcNow.AddDays(-10);
        clients.Radarr["radarr"] = new FakeRadarrClient
        {
            Movies = [new RadarrMovie(1, "Heat", null, 1995, null, 170, 949, null, null, added, true, new RadarrMovieFile(5, "/movies/Heat.mkv", 1000, added), [])]
        };
        clients.Radarr["radarr-4k"] = new FakeRadarrClient
        {
            Movies = [new RadarrMovie(1, "Heat", null, 1995, null, 170, 949, null, null, added, true, new RadarrMovieFile(105, "/movies/Heat.2160p.mkv", 5000, added), [])]
        };

        var sonarr = new FakeSonarrClient
        {
            Series = [new SonarrSeries(6, "Mayfair Witches", null, 2023, null, 45, 100, null, null, null, added, [new SonarrSeason(1, []), new SonarrSeason(2, [])], [])]
        };
        sonarr.Episodes[6] =
        [
            Episode(11, 1, 1, 21), Episode(12, 1, 2, 22), Episode(13, 1, 3, null), Episode(14, 2, 1, 24)
        ];
        sonarr.Files[6] = [File(21, "s1e1"), File(22, "s1e2"), File(24, "s2e1")];
        clients.Sonarr["sonarr"] = sonarr;

        var factory = new AkiraApiFactory(options)
        {
            Instances =
            [
                FakeArrClientFactory.Instance("radarr", ArrKind.Radarr),
                FakeArrClientFactory.Instance("radarr-4k", ArrKind.Radarr),
                FakeArrClientFactory.Instance("sonarr", ArrKind.Sonarr)
            ],
            TestServices = services => services.Replace(ServiceDescriptor.Singleton<IArrClientFactory>(clients))
        };

        var admin = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILibrarySyncService>().SyncAsync(TestContext.Current.CancellationToken);
        }

        async Task<Guid> Find(string query) =>
            (await GetAsync(admin, $"/api/v1/items?{query}")).Body.GetProperty("items")[0].GetProperty("id").GetGuid();

        var series = await Find("type=series");
        var season1 = (await GetAsync(admin, $"/api/v1/items/{series}/children")).Body.GetProperty("items").EnumerateArray()
            .Single(s => s.GetProperty("seasonNumber").GetInt32() == 1).GetProperty("id").GetGuid();
        var episode1 = (await GetAsync(admin, $"/api/v1/items/{season1}/children")).Body.GetProperty("items")[0].GetProperty("id").GetGuid();

        return new StartedApi(factory, admin, clients, await Find("type=movie"), series, season1, episode1, database);

        static SonarrEpisode Episode(int id, int season, int number, int? fileId) =>
            new(id, 6, null, season, number, $"Episode {number}", null, new DateOnly(2023, 1, number), 45, fileId is not null, fileId, []);

        static SonarrEpisodeFile File(int id, string name) => new(id, 6, $"/tv/Mayfair Witches/{name}.mkv", 1000, DateTimeOffset.UtcNow.AddDays(-5));
    }

    protected sealed record StartedApi(
        AkiraApiFactory Factory,
        HttpClient Admin,
        FakeArrClientFactory Clients,
        Guid Heat,
        Guid Series,
        Guid Season1,
        Guid Episode1,
        IAsyncDisposable? Database) : IAsyncDisposable
    {
        public FakeRadarrClient Radarr => Clients.Radarr["radarr"];
        public FakeRadarrClient Radarr4K => Clients.Radarr["radarr-4k"];
        public FakeSonarrClient Sonarr => Clients.Sonarr["sonarr"];

        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            await Factory.DisposeAsync();
            if (Database is not null)
            {
                await Database.DisposeAsync();
            }
        }
    }
}
