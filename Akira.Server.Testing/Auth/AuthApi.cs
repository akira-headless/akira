using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Akira.Server.Testing.Auth;

/// <summary>
/// Sends JSON to the API and reads the answers, for the auth flow tests.
/// </summary>
public static class AuthApi
{
    public static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> PostAsync(
        HttpClient client,
        string url,
        object body)
    {
        var response = await client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        return (response.StatusCode, await ReadAsync(response), response);
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        return (response.StatusCode, await ReadAsync(response));
    }

    public static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    /// <summary>
    /// Starts pairing on an anonymous client and returns what the device would keep.
    /// </summary>
    public static async Task<(Guid Id, string Code, string PollSecret)> StartPairingAsync(HttpClient anonymous, string name = "Living room TV",
        string client = "Kodi")
    {
        var (status, body, _) = await PostAsync(anonymous, "/api/v1/auth/pairings",
            new { deviceName = name, client, clientVersion = "21.1", platform = "Android TV" });
        Assert.Equal(HttpStatusCode.Created, status);
        return (body.GetProperty("pairingId").GetGuid(), body.GetProperty("code").GetString()!,
            body.GetProperty("pollSecret").GetString()!);
    }

    public static Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> PollAsync(
        HttpClient anonymous,
        (Guid Id, string Code, string PollSecret) pairing) =>
        PostAsync(anonymous, $"/api/v1/auth/pairings/{pairing.Id}/token", new { pollSecret = pairing.PollSecret });

    /// <summary>
    /// Pairs a device with the admin client approving it, and returns its token.
    /// </summary>
    public static async Task<string> PairAsync(HttpClient admin, HttpClient anonymous, string kind, Guid? userId = null, string client = "Kodi")
    {
        var pairing = await StartPairingAsync(anonymous, client: client);
        var (approved, _, _) = await PostAsync(admin, "/api/v1/auth/pairings/approve", new { code = pairing.Code, kind, userId });
        Assert.Equal(HttpStatusCode.OK, approved);

        var (status, body, _) = await PollAsync(anonymous, pairing);
        Assert.Equal(HttpStatusCode.OK, status);
        return body.GetProperty("token").GetString()!;
    }

    public static async Task<Guid> CreateUserAsync(HttpClient admin, string name, string? pin = null, string role = "user")
    {
        var (status, body, _) = await PostAsync(admin, "/api/v1/users", new { name, role, pin });
        Assert.Equal(HttpStatusCode.Created, status);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
