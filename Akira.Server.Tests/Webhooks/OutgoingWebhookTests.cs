using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Akira.Core.Auth;
using Akira.Server.Events;
using Akira.Infrastructure.Configuration;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Events;
using Akira.Server.Services.Webhooks;
using Akira.Server.Workers;
using Akira.Testing;
using Akira.Server.Testing;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Webhooks;

public abstract class OutgoingWebhookTests
{
    protected abstract Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task Create_ShowsTheSecretOnce_AndRefusesWhatCannotWork()
    {
        await using var api = await StartAsync();

        var (status, body, _) = await PostAsync(api.Admin, "/api/v1/outgoing-webhooks",
            new { name = "Home Assistant", url = "http://ha.local:8123/api/webhook/akira", eventTypes = new[] { "playback.started" } });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.StartsWith("whsec_", body.GetProperty("secret").GetString());
        var listed = (await GetAsync(api.Admin, "/api/v1/outgoing-webhooks")).Body.GetRawText();
        Assert.Contains("Home Assistant", listed);
        Assert.DoesNotContain("whsec_", listed);

        foreach (var bad in new object[]
                 {
                     new { name = "x", url = "ftp://example.com" },
                     new { name = "x", url = "https://user:password@example.com/hook" },
                     new { name = "x", url = "https://example.com/hook", eventTypes = new[] { "everything" } },
                     new { name = "", url = "https://example.com/hook" }
                 })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(api.Admin, "/api/v1/outgoing-webhooks", bad)).Status);
        }

        using var anonymous = api.Factory.CreateAnonymousClient();
        var userToken = await PairAsync(api.Admin, anonymous, "personal", await CreateUserAsync(api.Admin, "Kid"));
        using var kid = api.Factory.CreateClient(userToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(kid, "/api/v1/outgoing-webhooks")).Status);
    }

    [Fact]
    public async Task Events_ArriveInOrder_Signed_AndOnlyTheTypesAskedFor()
    {
        await using var api = await StartAsync();
        await Publish(api, new EventDraft(EventTypes.ScanProgress, new { before = true }));
        var secret = (await PostAsync(api.Admin, "/api/v1/outgoing-webhooks",
            new { name = "Logger", url = "https://hooks.example/akira", eventTypes = new[] { "playback.started", "playback.stopped" } }))
            .Body.GetProperty("secret").GetString()!;
        // Events of one user point at a real user, the events table has a foreign key to it
        var userId = (await GetAsync(api.Admin, "/api/v1/users/me")).Body.GetProperty("user").GetProperty("id").GetGuid();

        await Publish(api,
            new EventDraft(EventTypes.ScanProgress, new { skipped = true }),
            new EventDraft(EventTypes.PlaybackStarted, Playback(), userId),
            new EventDraft(EventTypes.PlaybackStopped, Playback() with { Played = true }, userId));
        await Deliver(api);

        // The event from before the webhook existed and the scan in between are not delivered
        Assert.Equal(["playback.started", "playback.stopped"], api.Receiver.Requests.Select(r => r.Headers.GetValues("X-Akira-Event").Single()));
        var request = api.Receiver.Requests[0];
        var body = api.Receiver.Bodies[0]!;
        var delivery = JsonDocument.Parse(body).RootElement;
        Assert.Equal(userId, delivery.GetProperty("userId").GetGuid());
        Assert.Equal("Heat", delivery.GetProperty("payload").GetProperty("identityKey").GetString());
        Assert.Equal(delivery.GetProperty("sequence").GetInt64().ToString(), request.Headers.GetValues("X-Akira-Delivery").Single());

        var signature = request.Headers.GetValues(WebhookSignature.Header).Single();
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(signature.Split(',')[0]["t=".Length..]));
        Assert.Equal(WebhookSignature.Create(secret, timestamp, Encoding.UTF8.GetBytes(body)), signature);

        var listed = (await GetAsync(api.Admin, "/api/v1/outgoing-webhooks")).Body[0];
        Assert.Equal(delivery.GetProperty("sequence").GetInt64() + 1, listed.GetProperty("lastSequence").GetInt64());
    }

    [Fact]
    public async Task AFailingReceiver_IsRetriedLater_AndLaterEventsWaitBehindIt()
    {
        var answers = new Queue<HttpStatusCode>([HttpStatusCode.InternalServerError, HttpStatusCode.OK, HttpStatusCode.OK]);
        await using var api = await StartAsync(answers);
        await PostAsync(api.Admin, "/api/v1/outgoing-webhooks", new { name = "Flaky", url = "https://hooks.example/flaky" });

        await Publish(api, new EventDraft(EventTypes.PlaybackStarted, Playback()), new EventDraft(EventTypes.PlaybackStopped, Playback()));
        await Deliver(api);
        var failed = (await GetAsync(api.Admin, "/api/v1/outgoing-webhooks")).Body[0];
        await Deliver(api);

        Assert.Single(api.Receiver.Requests);
        Assert.Equal((1, 500), (failed.GetProperty("consecutiveFailures").GetInt32(), failed.GetProperty("lastStatusCode").GetInt32()));
        Assert.Contains("500", failed.GetProperty("lastError").GetString());

        api.Time.Advance(TimeSpan.FromSeconds(11));
        await Deliver(api);

        Assert.Equal(["playback.started", "playback.started", "playback.stopped"],
            api.Receiver.Requests.Select(r => r.Headers.GetValues("X-Akira-Event").Single()));
        Assert.Equal(0, (await GetAsync(api.Admin, "/api/v1/outgoing-webhooks")).Body[0].GetProperty("consecutiveFailures").GetInt32());
    }

    [Fact]
    public async Task ASlowReceiver_DoesNotHoldBackTheOthers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource();
        var fastGotIt = new TaskCompletionSource();
        await using var api = await StartAsync(async (request, token) =>
        {
            if (request.RequestUri!.Host == "slow.example")
            {
                await release.Task.WaitAsync(token);
            }
            else
            {
                fastGotIt.TrySetResult();
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await PostAsync(api.Admin, "/api/v1/outgoing-webhooks", new { name = "Slow", url = "https://slow.example/hook" });
        await PostAsync(api.Admin, "/api/v1/outgoing-webhooks", new { name = "Fast", url = "https://fast.example/hook" });
        using var worker = ActivatorUtilities.CreateInstance<OutgoingWebhookWorker>(api.Factory.Services);
        await worker.StartAsync(cancellationToken);

        try
        {
            await Publish(api, new EventDraft(EventTypes.PlaybackStarted, Playback()));

            await fastGotIt.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.False(release.Task.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await worker.StopAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Test_SendsNow_AndRotatingGivesANewSecret()
    {
        await using var api = await StartAsync();
        var created = (await PostAsync(api.Admin, "/api/v1/outgoing-webhooks", new { name = "Tool", url = "https://hooks.example/tool" })).Body;
        var id = created.GetProperty("webhook").GetProperty("id").GetGuid();

        var test = (await PostAsync(api.Admin, $"/api/v1/outgoing-webhooks/{id}/test", new { })).Body;
        var rotated = (await PostAsync(api.Admin, $"/api/v1/outgoing-webhooks/{id}/rotate-secret", new { })).Body;

        Assert.Equal((true, 200), (test.GetProperty("succeeded").GetBoolean(), test.GetProperty("statusCode").GetInt32()));
        Assert.Equal("webhook.test", api.Receiver.Requests.Single().Headers.GetValues("X-Akira-Event").Single());
        Assert.NotEqual(created.GetProperty("secret").GetString(), rotated.GetProperty("secret").GetString());
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(api.Admin, HttpMethod.Delete, $"/api/v1/outgoing-webhooks/{id}"));
    }

    private static PlaybackPayload Playback() => new(Guid.NewGuid(), Guid.NewGuid(), "Heat", Guid.NewGuid(), null, 0, false, null);

    private static async Task Publish(WebhookApi api, params EventDraft[] events) =>
        await api.Factory.Services.GetRequiredService<IEventPublisher>().PublishAsync(events);

    private static async Task Deliver(WebhookApi api)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = api.Factory.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutgoingWebhookDispatcher>();
        foreach (var id in await dispatcher.ListEnabledAsync(cancellationToken))
        {
            while (await dispatcher.DeliverAsync(id, cancellationToken) == OutgoingWebhookRound.More)
            {
            }
        }
    }

    private Task<WebhookApi> StartAsync(Queue<HttpStatusCode>? answers = null) =>
        StartAsync((_, _) => Task.FromResult(new HttpResponseMessage(answers is { Count: > 0 } ? answers.Dequeue() : HttpStatusCode.OK)));

    private async Task<WebhookApi> StartAsync(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var (options, database) = await CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var keys = Directory.CreateTempSubdirectory("akira_keys_").FullName;
        var receiver = new StubHttpHandler(respond);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero));
        var factory = new AkiraApiFactory(options)
        {
            Time = time,
            Settings = { ["Auth:KeysPath"] = keys },
            TestServices = services => services.AddHttpClient(OutgoingWebhookDispatcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => receiver)
        };
        return new WebhookApi(factory, factory.CreateClient(), receiver, time, keys, database);
    }

    protected sealed record WebhookApi(
        AkiraApiFactory Factory,
        HttpClient Admin,
        StubHttpHandler Receiver,
        FakeTimeProvider Time,
        string Keys,
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

            Directory.Delete(Keys, recursive: true);
        }
    }
}
