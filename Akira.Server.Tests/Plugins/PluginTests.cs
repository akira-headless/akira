using System.Net;
using System.Reflection;
using System.Text.Json;
using Akira.Contracts.Accounts;
using Akira.Contracts.Bridges;
using Akira.Contracts.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Server.Bridges;
using Akira.Server.Plugins;
using Akira.Server.Testing;
using Akira.Server.Testing.Playback;
using Xunit;

namespace Akira.Server.Tests.Plugins;

public class PluginTests
{
    [Fact]
    public async Task Plugins_ListsTheBuiltIns_AndSkipsADllThatDoesNotLoad_WithoutEverShowingAValue()
    {
        var folder = Directory.CreateTempSubdirectory("akira_plugins_").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "broken.dll"), "not an assembly", TestContext.Current.CancellationToken);
            await using var factory = new AkiraApiFactory
            {
                Settings = { ["Plugins:Path"] = folder, ["Subtitles:OpenSubtitles:ApiKey"] = "secret-api-key-value" }
            };
            using var admin = factory.CreateClient();

            var response = await admin.GetAsync("/api/v1/plugins", TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var overview = JsonDocument.Parse(body).RootElement;
            var plugins = overview.GetProperty("plugins").EnumerateArray().ToDictionary(p => p.GetProperty("key").GetString()!);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(("active", "built-in", "subtitleSource"), (plugins["opensubtitles"].GetProperty("status").GetString(),
                plugins["opensubtitles"].GetProperty("source").GetString(), plugins["opensubtitles"].GetProperty("provides")[0].GetString()));
            Assert.Equal("notConfigured", plugins["lingarr"].GetProperty("status").GetString());
            Assert.Equal("broken.dll", overview.GetProperty("skipped").EnumerateArray().Single().GetProperty("file").GetString());
            Assert.DoesNotContain("secret-api-key-value", body);

            using var anonymous = factory.CreateAnonymousClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/plugins", TestContext.Current.CancellationToken)).StatusCode);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ABridge_AnswersOnlyUnderItsOwnPath_AnonymousAndOutOfTheDocument_AndAPluginHasNoOtherControllers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new AkiraApiFactory
        {
            TestServices = services =>
            {
                // This assembly stands in for the DLL of a switched on bridge
                var catalog = (PluginCatalog)services.Single(service => service.ServiceType == typeof(PluginCatalog)).ImplementationInstance!;
                catalog.Plugins.Add(new LoadedPlugin("probe", new PluginManifest("Probe", "", []), "1.0.0", "Probe.dll", new EnabledSettings(),
                    [LoadedPlugin.BridgeKind], typeof(ProbeBridge).Assembly));
                services.AddKeyedSingleton<IClientBridge>("probe", new ProbeBridge());
                services.AddMvcCore().AddApplicationPart(typeof(ProbeBridge).Assembly);
            }
        };
        using var admin = factory.CreateClient();
        using var anonymous = factory.CreateAnonymousClient();

        Assert.Equal("escaped", await anonymous.GetStringAsync("/bridges/probe/api/v1/escape", cancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/escape", cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/sneak", cancellationToken)).StatusCode);
        var document = await admin.GetStringAsync("/openapi/v1.json", cancellationToken);
        Assert.DoesNotContain("escape", document);
        Assert.DoesNotContain("sneak", document);
    }

    [Fact]
    public async Task ABridgeThatCannotBeMade_IsSkippedWithItsOwnReason_AndTheServerRuns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new AkiraApiFactory
        {
            TestServices = services =>
            {
                var catalog = (PluginCatalog)services.Single(service => service.ServiceType == typeof(PluginCatalog)).ImplementationInstance!;
                catalog.Plugins.Add(new LoadedPlugin("broken", new PluginManifest("Broken", "", []), "1.0.0", "Broken.dll", new EnabledSettings(),
                    [LoadedPlugin.BridgeKind], typeof(ProbeBridge).Assembly));

                // What a constructor that throws looks like once reflection made it
                services.AddKeyedSingleton<IClientBridge>("broken",
                    (_, _) => throw new TargetInvocationException(new InvalidOperationException("No network card")));
                services.AddMvcCore().AddApplicationPart(typeof(ProbeBridge).Assembly);
            }
        };
        using var admin = factory.CreateClient();

        var overview = JsonDocument.Parse(await admin.GetStringAsync("/api/v1/plugins", cancellationToken)).RootElement;
        var skipped = overview.GetProperty("skipped").EnumerateArray().Single();
        Assert.Equal(("Broken.dll", "Its bridge could not be made: No network card"),
            (skipped.GetProperty("file").GetString(), skipped.GetProperty("reason").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/bridges/broken/api/v1/escape", cancellationToken)).StatusCode);
        Assert.Equal(0, JsonDocument.Parse(await admin.GetStringAsync("/api/v1/bridges", cancellationToken)).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ASignedUrl_EndsWithTheCredentialItWasMadeFor_NotOnlyWithItsDevice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mediaRoot = Directory.CreateTempSubdirectory("akira_media_").FullName;
        try
        {
            await using var factory = new AkiraApiFactory();
            using var admin = factory.CreateClient();
            await admin.GetAsync("/api/v1/plugins", cancellationToken);

            string url;
            Guid tokenId;
            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
                var ids = await PlaybackSeed.SeedAsync(dbContext, mediaRoot, cancellationToken);
                var user = await dbContext.Users.SingleAsync(cancellationToken);

                // A profile session on a shared device: the device lives on after the session ends
                var device = new Device { Name = "Living room", Client = "test", Kind = DeviceKind.Shared };
                var session = new DeviceToken { DeviceId = device.Id, UserId = user.Id, TokenHash = "hash", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
                dbContext.Devices.Add(device);
                dbContext.DeviceTokens.Add(session);
                await dbContext.SaveChangesAsync(cancellationToken);
                tokenId = session.Id;

                var caller = new Caller(user.Id, user.Name, device.Id, true) { TokenId = session.Id };
                url = scope.ServiceProvider.GetRequiredService<SignedUrls>().FileUrl(caller, ids.HeatFullHd, "Heat.mkv", TimeSpan.FromDays(1));
            }

            var heat = Path.Combine(mediaRoot, "movies", "Heat (1995)", "Heat.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(heat)!);
            await File.WriteAllBytesAsync(heat, new byte[100], cancellationToken);

            using var player = factory.CreateAnonymousClient();
            Assert.Equal(HttpStatusCode.OK, (await player.GetAsync(url, cancellationToken)).StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<AkiraDbContext>().DeviceTokens.Where(t => t.Id == tokenId)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await player.GetAsync(url, cancellationToken)).StatusCode);
        }
        finally
        {
            Directory.Delete(mediaRoot, recursive: true);
        }
    }

    private sealed class EnabledSettings : IPluginSettings
    {
        public string Section => "Plugins:probe";

        public bool Enabled => true;

        public string? Get(string key) => null;
    }
}
