using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Akira.Infrastructure.Configuration;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Radarr;
using Akira.Infrastructure.Interfaces.Integrations;
using Akira.Infrastructure.Interfaces.Sync;
using Akira.Server.Interfaces.Services;
using Akira.Testing;
using Akira.Server.Webhooks;
using Akira.Server.Testing;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Events;

public abstract class WebhookTests
{
    private const string RadarrSecret = "radarr-webhook-secret-0123";
    private const string SonarrSecret = "sonarr-webhook-secret-0123";

    protected abstract Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task TestEvent_FromRadarr_IsAcceptedButRefreshesNothing()
    {
        await using var api = await StartAsync();

        // The real test event names movie 1, which must not be refreshed
        var (status, body) = await SendWebhookAsync(api, "radarr", RadarrSecret, Fixture("radarr-webhook-test.json"));

        Assert.Equal((HttpStatusCode.Accepted, "Test", false),
            (status, body.GetProperty("eventType").GetString(), body.GetProperty("queued").GetBoolean()));
        Assert.False(api.Queue.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData("radarr", "Download", "radarr-webhook-test.json")]
    [InlineData("radarr", "MovieDelete", "radarr-webhook-test.json")]
    [InlineData("sonarr", "Download", "sonarr-webhook-test.json")]
    [InlineData("sonarr", "EpisodeFileDelete", "sonarr-webhook-test.json")]
    public async Task ChangeEvents_QueueARefreshOfTheirMovieOrSeries(string instance, string eventType, string fixture)
    {
        await using var api = await StartAsync();

        // The shape of the real test event, with the event type of a change
        var (status, body) = await SendWebhookAsync(api, instance, instance == "radarr" ? RadarrSecret : SonarrSecret,
            Fixture(fixture, eventType));

        Assert.Equal((HttpStatusCode.Accepted, true), (status, body.GetProperty("queued").GetBoolean()));
        Assert.True(api.Queue.Reader.TryRead(out var refresh));
        Assert.Equal(new LibraryRefresh(instance, 1), refresh);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-secret-but-long-enough")]
    [InlineData("sonarr-webhook-secret-0123")]
    public async Task WrongOrMissingSecret_IsUnauthorized(string? secret)
    {
        await using var api = await StartAsync();

        var (status, _, response) = await SendWebhookRawAsync(api, "radarr", secret, Fixture("radarr-webhook-test.json", "Download"));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("Basic", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.False(api.Queue.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData("radarr-4k")]
    [InlineData("unknown")]
    public async Task InstanceWithoutASecret_OrUnknown_IsNotFound(string instance)
    {
        await using var api = await StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await SendWebhookAsync(api, instance, RadarrSecret, Fixture("radarr-webhook-test.json"))).Status);
    }

    [Theory]
    [InlineData("{\"movie\":{\"id\":1}}")]
    [InlineData("[1,2]")]
    [InlineData("not json")]
    public async Task SomethingElseThanAWebhook_IsABadRequest(string body)
    {
        await using var api = await StartAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendWebhookRawAsync(api, "radarr", RadarrSecret, body)).Status);
    }

    [Fact]
    public async Task Refresh_PublishesWhatChanged_AndWakesTheProbeWorker()
    {
        await using var api = await StartAsync();
        var radarr = api.Clients.Radarr["radarr"];
        radarr.Movies = [Movie(5, "/movies/Heat (1995)/Heat Bluray-1080p.mkv")];
        using (var scope = api.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILibrarySyncService>().SyncAsync(TestContext.Current.CancellationToken);
        }

        var latest = (await GetAsync(api.Admin, "/api/v1/events?since=0")).Body.GetProperty("latest").GetInt64();
        radarr.Movies = [Movie(6, "/movies/Heat (1995)/Heat Remux-2160p.mkv")];
        await RefreshAsync(api, new LibraryRefresh("radarr", 1));

        var events = (await GetAsync(api.Admin, $"/api/v1/events?since={latest}")).Body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["library.item.updated", "library.file.replaced", "scan.progress"], events.Select(e => e.GetProperty("type").GetString()));
        Assert.Equal("movie:tmdb:949", events[0].GetProperty("payload").GetProperty("identityKey").GetString());
        Assert.Equal("radarr", events[1].GetProperty("payload").GetProperty("instance").GetString());
        var scan = events[2].GetProperty("payload");
        Assert.Equal(("refresh", "succeeded", 1), (scan.GetProperty("kind").GetString(), scan.GetProperty("status").GetString(),
            scan.GetProperty("instances")[0].GetProperty("updated").GetInt32()));
        Assert.True(await api.Factory.Services.GetRequiredService<ProbeTrigger>().WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));

        // Deleted in Radarr: the next webhook removes it
        radarr.Movies = [];
        await RefreshAsync(api, new LibraryRefresh("radarr", 1));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(api.Admin, "/api/v1/items")).Status);
        Assert.Equal(0, (await GetAsync(api.Admin, "/api/v1/items")).Body.GetProperty("total").GetInt32());
        var removed = (await GetAsync(api.Admin, $"/api/v1/events?since={latest}")).Body.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Contains("library.item.removed", removed);
    }

    [Fact]
    public async Task SyncRequest_FromAClient_WakesTheSyncWorkerOnce()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        using var viewer = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "personal", await CreateUserAsync(api.Admin, "Viewer")));

        var first = await PostAsync(viewer, "/api/v1/library/sync", new { });
        var second = await PostAsync(viewer, "/api/v1/library/sync", new { });

        Assert.Equal((HttpStatusCode.Accepted, true), (first.Status, first.Body.GetProperty("queued").GetBoolean()));
        Assert.Equal((HttpStatusCode.Accepted, false), (second.Status, second.Body.GetProperty("queued").GetBoolean()));
        var trigger = api.Factory.Services.GetRequiredService<LibrarySyncTrigger>();
        Assert.True(await trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        Assert.False(await trigger.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    private static async Task RefreshAsync(StartedApi api, params LibraryRefresh[] refreshes)
    {
        using var scope = api.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ILibraryRefreshService>().RefreshAsync(refreshes, TestContext.Current.CancellationToken);
    }

    private static RadarrMovie Movie(int fileId, string path) =>
        new(1, "Heat", null, 1995, null, 170, 949, null, null, DateTimeOffset.UtcNow.AddDays(-1), true,
            new RadarrMovieFile(fileId, path, 1000, null), []);

    private static string Fixture(string name, string? eventType = null)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        if (eventType is null)
        {
            return text;
        }

        var node = JsonNode.Parse(text)!;
        node["eventType"] = eventType;
        return node.ToJsonString();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendWebhookAsync(StartedApi api, string instance, string secret, string body)
    {
        var (status, json, _) = await SendWebhookRawAsync(api, instance, secret, body);
        return (status, json);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> SendWebhookRawAsync(
        StartedApi api, string instance, string? secret, string body)
    {
        using var client = api.Factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/{instance}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (secret is not null)
        {
            // How Radarr and Sonarr send the username and password of a webhook connection
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"akira:{secret}")));
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone(), response);
    }

    private async Task<StartedApi> StartAsync()
    {
        var (options, database) = await CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var clients = new FakeArrClientFactory();
        clients.Radarr["radarr"] = new FakeRadarrClient();
        clients.Radarr["radarr-4k"] = new FakeRadarrClient();
        clients.Sonarr["sonarr"] = new FakeSonarrClient();

        var radarr = FakeArrClientFactory.Instance("radarr", ArrKind.Radarr);
        radarr.WebhookSecret = RadarrSecret;
        var sonarr = FakeArrClientFactory.Instance("sonarr", ArrKind.Sonarr);
        sonarr.WebhookSecret = SonarrSecret;

        var factory = new AkiraApiFactory(options)
        {
            Instances = [radarr, FakeArrClientFactory.Instance("radarr-4k", ArrKind.Radarr), sonarr],
            TestServices = services => services.Replace(ServiceDescriptor.Singleton<IArrClientFactory>(clients))
        };

        return new StartedApi(factory, factory.CreateClient(), clients, factory.Services.GetRequiredService<LibraryRefreshQueue>(), database);
    }

    protected sealed record StartedApi(
        AkiraApiFactory Factory,
        HttpClient Admin,
        FakeArrClientFactory Clients,
        LibraryRefreshQueue Queue,
        IAsyncDisposable? Database) : IAsyncDisposable
    {
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
