using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Akira.Infrastructure.Data;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests.Endpoints;

public class FileEndpointsTests
{
    [Fact]
    public async Task Get_ListsTracksInOrderAndChapters()
    {
        var (client, ids, factory) = await StartAsync();
        await using var disposeFactory = factory;
        using var disposeClient = client;

        var response = await client.GetAsync($"/api/v1/files/{ids.Heat4KFile}", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var file = body.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((ids.Heat, "radarr-4k", "Radarr 4K", "probed"),
            (file.GetProperty("itemId").GetGuid(), file.GetProperty("instance").GetString(),
                file.GetProperty("instanceName").GetString(), file.GetProperty("probeStatus").GetString()));

        var streams = file.GetProperty("streams").EnumerateArray().ToList();
        Assert.Equal(["video", "audio", "audio", "subtitle", "subtitle"], streams.Select(s => s.GetProperty("kind").GetString()));
        Assert.Equal("a1000000000000000000000000000000", streams[1].GetProperty("id").GetString());
        Assert.True(streams[2].GetProperty("isCommentary").GetBoolean());
        Assert.True(streams[3].GetProperty("isForced").GetBoolean());
        Assert.False(streams[3].GetProperty("isTextBased").GetBoolean());
        Assert.True(streams[4].GetProperty("isExternal").GetBoolean());
        Assert.Equal(1500, streams[4].GetProperty("eventCount").GetInt32());

        // Neither the ffprobe index nor a server path reaches a client here
        Assert.All(streams, s => Assert.False(s.TryGetProperty("indexHint", out _)));
        Assert.All(streams, s => Assert.False(s.TryGetProperty("externalPath", out _)));
        Assert.False(file.TryGetProperty("path", out _));

        Assert.Equal(["Opening", "The Heist"], file.GetProperty("chapters").EnumerateArray()
            .Select(c => c.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task Get_Unknown_IsNotFound()
    {
        var (client, _, factory) = await StartAsync();
        await using var disposeFactory = factory;
        using var disposeClient = client;

        var response = await client.GetAsync($"/api/v1/files/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<(HttpClient Client, LibrarySeed.Ids Ids, AkiraApiFactory Factory)> StartAsync()
    {
        var factory = new AkiraApiFactory { Instances = LibrarySeed.Instances };
        using var scope = factory.Services.CreateScope();
        var ids = await LibrarySeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AkiraDbContext>(),
            TestContext.Current.CancellationToken);
        return (factory.CreateClient(), ids, factory);
    }
}
