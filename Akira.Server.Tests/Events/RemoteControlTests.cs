using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Akira.Server.Events;
using Akira.Testing;
using Akira.Server.Testing;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Events;

public class RemoteControlTests
{
    [Fact]
    public async Task APhone_ControlsTheUsersOwnConnectedTv_AndNobodyElseDoes()
    {
        await using var factory = new AkiraApiFactory(null);
        using var admin = factory.CreateClient();
        using var anonymous = factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(admin, "Kid");
        var tvToken = await PairAsync(admin, anonymous, "personal", kid);
        using var tv = factory.CreateClient(tvToken);
        using var phone = factory.CreateClient(await PairAsync(admin, anonymous, "personal", kid));
        var tvId = (await GetAsync(tv, "/api/v1/users/me")).Body.GetProperty("device").GetProperty("id").GetGuid();

        using var socket = await ConnectAsync(factory, tvToken);
        Assert.Equal(EventTypes.StreamReady, (await ReceiveAsync(socket)).GetProperty("type").GetString());

        var listed = (await GetAsync(phone, "/api/v1/remote/devices")).Body.EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();
        var pause = await PostAsync(phone, $"/api/v1/remote/devices/{tvId}/commands", new { type = "pause" });
        var command = await ReceiveAsync(socket);
        // The admin is another user, even with the tv's id
        var byAdmin = await PostAsync(admin, $"/api/v1/remote/devices/{tvId}/commands", new { type = "pause" });
        var playWithoutItem = await PostAsync(phone, $"/api/v1/remote/devices/{tvId}/commands", new { type = "play" });

        Assert.Equal([tvId], listed);
        Assert.Equal(HttpStatusCode.NoContent, pause.Status);
        Assert.Equal((EventTypes.RemoteCommand, 0L, "pause"),
            (command.GetProperty("type").GetString(), command.GetProperty("sequence").GetInt64(),
                command.GetProperty("payload").GetProperty("type").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, byAdmin.Status);
        Assert.Equal(HttpStatusCode.BadRequest, playWithoutItem.Status);
        Assert.Empty((await GetAsync(admin, "/api/v1/remote/devices")).Body.EnumerateArray());
    }

    private static async Task<WebSocket> ConnectAsync(AkiraApiFactory factory, string bearer)
    {
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {bearer}";
        return await client.ConnectAsync(new Uri(factory.Server.BaseAddress, "api/v1/events/ws?remote=true"), TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var buffer = new byte[64 * 1024];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count)).RootElement.Clone();
    }
}
