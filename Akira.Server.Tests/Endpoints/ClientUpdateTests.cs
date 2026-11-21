using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Akira.Infrastructure.Updates;
using Akira.Server.Testing;
using Akira.Testing;
using Xunit;

namespace Akira.Server.Tests.Endpoints;

public class ClientUpdateTests
{
    [Fact]
    public async Task Check_FollowsTheSourceToItsStorage_AndServesTheBuildUntilAVerifiedNewerOne()
    {
        var apk = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
        var manifest = new { build = 5, version = "0.1.0 (2026-11-16 10:00)", file = "akira.apk", sha256 = Convert.ToHexStringLower(SHA256.HashData(apk)) };
        var stub = new StubHttpHandler((request, _) => Task.FromResult(request.RequestUri!.AbsoluteUri switch
        {
            "https://updates.example/akira/update.json" => StubHttpHandler.Response(HttpStatusCode.OK, JsonSerializer.Serialize(manifest), "application/json"),
            // As GitHub sends a release file on to its storage
            "https://updates.example/akira/akira.apk" => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://storage.example/blob?sig=1") } },
            "https://storage.example/blob?sig=1" => StubHttpHandler.Image(apk, "application/octet-stream"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));
        var folder = Path.Combine(Path.GetTempPath(), "akira-updates-" + Guid.NewGuid().ToString("N"));
        await using var factory = new AkiraApiFactory
        {
            Settings = new()
            {
                ["ClientUpdates:Path"] = folder,
                ["ClientUpdates:Sources:android:ManifestUrl"] = "https://updates.example/akira/update.json",
                ["ClientUpdates:Sources:android:Authorization"] = "Basic dXNlcjp0b2tlbg=="
            },
            TestServices = services => services.AddHttpClient(ClientUpdateStore.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => stub)
        };
        try
        {
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/client-updates/android", TestContext.Current.CancellationToken)).StatusCode);
            var check = await client.PostAsync("/api/v1/client-updates/android/check", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, check.StatusCode);

            Assert.Equal(["Basic dXNlcjp0b2tlbg==", "Basic dXNlcjp0b2tlbg==", null],
                stub.Requests.Select(request => request.Headers.Authorization?.ToString()));
            using var update = JsonDocument.Parse(await client.GetStringAsync("/api/v1/client-updates/android", TestContext.Current.CancellationToken));
            Assert.Equal((5L, "akira.apk", 300L, "/api/v1/client-updates/android/file"),
                (update.RootElement.GetProperty("build").GetInt64(), update.RootElement.GetProperty("fileName").GetString(),
                    update.RootElement.GetProperty("sizeBytes").GetInt64(), update.RootElement.GetProperty("downloadUrl").GetString()));
            var file = await client.GetAsync("/api/v1/client-updates/android/file", TestContext.Current.CancellationToken);
            Assert.Equal(apk, await file.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            Assert.Equal(("application/vnd.android.package-archive", "akira.apk"),
                (file.Content.Headers.ContentType?.MediaType, file.Content.Headers.ContentDisposition?.FileName));

            // A build that does not match its manifest never replaces the one there is
            manifest = manifest with { build = 6, sha256 = new string('0', 64) };
            var failed = await client.PostAsync("/api/v1/client-updates/android/check", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Contains("SHA-256", await failed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            using var kept = JsonDocument.Parse(await client.GetStringAsync("/api/v1/client-updates/android", TestContext.Current.CancellationToken));
            Assert.Equal(5L, kept.RootElement.GetProperty("build").GetInt64());

            Assert.Equal(HttpStatusCode.NotFound,
                (await client.PostAsync("/api/v1/client-updates/linux/check", null, TestContext.Current.CancellationToken)).StatusCode);
            using var anonymous = factory.CreateAnonymousClient();
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.GetAsync("/api/v1/client-updates/android", TestContext.Current.CancellationToken)).StatusCode);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
