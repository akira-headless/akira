using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Akira.Server.Authentication;
using Akira.Server.Events;
using Akira.Infrastructure.Configuration;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using Akira.Server.Testing;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Events;

public abstract class EventFlowTests
{
    protected abstract Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task CatchUp_ReturnsEventsInTheOrderTheyWerePublished()
    {
        await using var api = await StartAsync();
        await PublishAsync(api, Item(EventTypes.ItemAdded, "a"), Item(EventTypes.ItemUpdated, "b"), Item(EventTypes.ItemRemoved, "c"));

        var page = (await GetAsync(api.Admin, "/api/v1/events?since=0")).Body;

        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([EventTypes.ItemAdded, EventTypes.ItemUpdated, EventTypes.ItemRemoved], items.Select(i => i.GetProperty("type").GetString()));
        Assert.Equal(["a", "b", "c"], items.Select(i => i.GetProperty("payload").GetProperty("identityKey").GetString()));
        var sequences = items.Select(i => i.GetProperty("sequence").GetInt64()).ToList();
        Assert.Equal(sequences.Order(), sequences);
        Assert.Equal((sequences[^1], false, false),
            (page.GetProperty("latest").GetInt64(), page.GetProperty("hasMore").GetBoolean(), page.GetProperty("resync").GetBoolean()));
        Assert.Equal("movie", items[0].GetProperty("payload").GetProperty("type").GetString());
    }

    [Fact]
    public async Task CatchUp_PagesWithHasMore()
    {
        await using var api = await StartAsync();
        await PublishAsync(api, Enumerable.Range(1, 5).Select(i => Item(EventTypes.ItemUpdated, $"k{i}")).ToArray());

        var first = (await GetAsync(api.Admin, "/api/v1/events?since=0&limit=2")).Body;
        var cursor = first.GetProperty("items")[1].GetProperty("sequence").GetInt64();
        var rest = (await GetAsync(api.Admin, $"/api/v1/events?since={cursor}&limit=200")).Body;

        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Equal(["k3", "k4", "k5"], rest.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("payload").GetProperty("identityKey").GetString()));
        Assert.False(rest.GetProperty("hasMore").GetBoolean());
    }

    [Theory]
    [InlineData("/api/v1/events?since=-1")]
    [InlineData("/api/v1/events?since=0&limit=0")]
    [InlineData("/api/v1/events?since=0&limit=201")]
    [InlineData("/api/v1/events")]
    public async Task CatchUp_WithABadCursorOrLimit_IsABadRequest(string url)
    {
        await using var api = await StartAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(api.Admin, url)).Status);
    }

    [Fact]
    public async Task CatchUp_OnlyShowsAUsersOwnEvents()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(api.Admin, "Kid");
        using var kidPhone = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "personal", kid));
        await PublishAsync(api,
            new EventDraft(EventTypes.TrackPreferenceChanged, new TrackPreferencePayload(Core.Enum.PreferenceScope.Global, null), kid),
            Item(EventTypes.ItemAdded, "everyone"));

        var forAdmin = Types((await GetAsync(api.Admin, "/api/v1/events?since=0")).Body);
        var forKid = Types((await GetAsync(kidPhone, "/api/v1/events?since=0")).Body);

        Assert.Equal([EventTypes.ItemAdded], forAdmin);
        Assert.Equal([EventTypes.TrackPreferenceChanged, EventTypes.ItemAdded], forKid);
    }

    [Fact]
    public async Task SharedDeviceWithoutAProfile_HearsAboutTheLibrary()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        using var tv = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "shared"));
        await PublishAsync(api, Item(EventTypes.ItemAdded, "everyone"));

        var (status, body) = await GetAsync(tv, "/api/v1/events?since=0");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([EventTypes.ItemAdded], Types(body));
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(anonymous, "/api/v1/events?since=0")).Status);
    }

    [Fact]
    public async Task CatchUp_AfterRetention_AsksTheClientToReload_AndKeepsTheNewestEvent()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var api = await StartAsync(time);
        await PublishAsync(api, Item(EventTypes.ItemAdded, "old1"), Item(EventTypes.ItemAdded, "old2"));
        var cursor = (await GetAsync(api.Admin, "/api/v1/events?since=0")).Body.GetProperty("items")[0].GetProperty("sequence").GetInt64();

        time.Advance(TimeSpan.FromDays(8));
        using (var scope = api.Factory.Services.CreateScope())
        {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IEventFeedService>().PruneAsync(TestContext.Current.CancellationToken));
        }

        var page = (await GetAsync(api.Admin, $"/api/v1/events?since={cursor - 1}")).Body;
        Assert.Equal((true, 0), (page.GetProperty("resync").GetBoolean(), page.GetProperty("items").GetArrayLength()));

        // The client reloaded and continues from latest, which still exists, so sequences never start over
        var latest = page.GetProperty("latest").GetInt64();
        await PublishAsync(api, Item(EventTypes.ItemAdded, "new"));
        var next = (await GetAsync(api.Admin, $"/api/v1/events?since={latest}")).Body;
        Assert.Equal(["new"], next.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("payload").GetProperty("identityKey").GetString()));
        Assert.True(next.GetProperty("items")[0].GetProperty("sequence").GetInt64() > latest);
    }

    [Fact]
    public async Task CatchUp_WithACursorFromAnotherDatabase_AsksTheClientToReload()
    {
        await using var api = await StartAsync();
        await PublishAsync(api, Item(EventTypes.ItemAdded, "a"));

        Assert.True((await GetAsync(api.Admin, "/api/v1/events?since=9999")).Body.GetProperty("resync").GetBoolean());
    }

    [Fact]
    public async Task Socket_ReplaysWhatWasMissed_ThenSaysReady_ThenSendsLiveEvents()
    {
        await using var api = await StartAsync();
        await PublishAsync(api, Item(EventTypes.ItemAdded, "missed1"), Item(EventTypes.ItemAdded, "missed2"));

        using var socket = await ConnectAsync(api, "?since=0", TestAdmin.ApiKey);
        var replayed = new[] { await ReceiveAsync(socket), await ReceiveAsync(socket) };
        var ready = await ReceiveAsync(socket);
        await PublishAsync(api, Item(EventTypes.ItemUpdated, "live"));
        var live = await ReceiveAsync(socket);

        Assert.Equal(["missed1", "missed2"], replayed.Select(m => m.GetProperty("payload").GetProperty("identityKey").GetString()));
        Assert.Equal((EventTypes.StreamReady, replayed[1].GetProperty("sequence").GetInt64()),
            (ready.GetProperty("type").GetString(), ready.GetProperty("sequence").GetInt64()));
        Assert.Equal(("live", EventTypes.ItemUpdated),
            (live.GetProperty("payload").GetProperty("identityKey").GetString(), live.GetProperty("type").GetString()));
        Assert.True(live.GetProperty("sequence").GetInt64() > ready.GetProperty("sequence").GetInt64());
    }

    [Fact]
    public async Task Socket_WithoutSince_StartsAtTheNewestEvent()
    {
        await using var api = await StartAsync();
        await PublishAsync(api, Item(EventTypes.ItemAdded, "before"));

        using var socket = await ConnectAsync(api, "", TestAdmin.ApiKey);
        var ready = await ReceiveAsync(socket);

        Assert.Equal(EventTypes.StreamReady, ready.GetProperty("type").GetString());
        Assert.Equal(ready.GetProperty("sequence").GetInt64(), ready.GetProperty("payload").GetProperty("latest").GetInt64());
    }

    [Fact]
    public async Task Socket_WithAGoneCursor_AsksTheClientToReload()
    {
        await using var api = await StartAsync();
        await PublishAsync(api, Item(EventTypes.ItemAdded, "a"));

        using var socket = await ConnectAsync(api, "?since=9999", TestAdmin.ApiKey);

        Assert.Equal(EventTypes.StreamResync, (await ReceiveAsync(socket)).GetProperty("type").GetString());
        Assert.Equal(EventTypes.StreamReady, (await ReceiveAsync(socket)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Socket_DoesNotSendAnotherUsersEvents()
    {
        await using var api = await StartAsync();
        var kid = await CreateUserAsync(api.Admin, "Kid");

        using var socket = await ConnectAsync(api, "", TestAdmin.ApiKey);
        await ReceiveAsync(socket);
        await PublishAsync(api,
            new EventDraft(EventTypes.TrackPreferenceChanged, new TrackPreferencePayload(Core.Enum.PreferenceScope.Global, null), kid),
            Item(EventTypes.ItemAdded, "everyone"));

        Assert.Equal(EventTypes.ItemAdded, (await ReceiveAsync(socket)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Socket_ClosesOnceItsDeviceIsRevoked()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var api = await StartAsync(time);
        using var anonymous = api.Factory.CreateAnonymousClient();
        var token = await PairAsync(api.Admin, anonymous, "personal", await CreateUserAsync(api.Admin, "Kid"));
        using var phone = api.Factory.CreateClient(token);
        var deviceId = (await GetAsync(phone, "/api/v1/users/me")).Body.GetProperty("device").GetProperty("id").GetGuid();

        using var socket = await ConnectAsync(api, "", null, token);
        await ReceiveAsync(socket);
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(api.Admin, HttpMethod.Delete, $"/api/v1/devices/{deviceId}"));
        time.Advance(EventSocket.CredentialCheckInterval);

        var closed = await socket.ReceiveAsync(new byte[1024], TestContext.Current.CancellationToken);
        Assert.Equal((WebSocketMessageType.Close, WebSocketCloseStatus.PolicyViolation), (closed.MessageType, closed.CloseStatus));
    }

    [Fact]
    public async Task Socket_NeedsACredential_AndAPlainGetIsABadRequest()
    {
        await using var api = await StartAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(api, "", null));
        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(api.Admin, "/api/v1/events/ws")).Status);
    }

    private static List<string?> Types(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString()).ToList();

    private static EventDraft Item(string type, string key) =>
        new(type, new LibraryItemPayload(Guid.NewGuid(), Core.Enum.MediaItemType.Movie, key, null, null));

    private static Task PublishAsync(StartedApi api, params EventDraft[] drafts) =>
        api.Factory.Services.GetRequiredService<IEventPublisher>().PublishAsync(drafts);

    private static async Task<WebSocket> ConnectAsync(StartedApi api, string query, string? apiKey, string? bearer = null)
    {
        var client = api.Factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            if (apiKey is not null)
            {
                request.Headers[AkiraAuthenticationHandler.ApiKeyHeader] = apiKey;
            }

            if (bearer is not null)
            {
                request.Headers.Authorization = $"Bearer {bearer}";
            }
        };

        return await client.ConnectAsync(new Uri(api.Factory.Server.BaseAddress, $"api/v1/events/ws{query}"), TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var buffer = new byte[64 * 1024];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count)).RootElement.Clone();
    }

    private async Task<StartedApi> StartAsync(TimeProvider? time = null)
    {
        var (options, database) = await CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var factory = new AkiraApiFactory(options) { Time = time };
        return new StartedApi(factory, factory.CreateClient(), database);
    }

    protected sealed record StartedApi(AkiraApiFactory Factory, HttpClient Admin, IAsyncDisposable? Database) : IAsyncDisposable
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
