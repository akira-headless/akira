using Microsoft.Extensions.Options;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests;

public class ConfigurationStartupTests
{
    [Fact]
    public async Task SyncIntervalBelowMinimum_FailsAtStartup()
    {
        await using var factory = new AkiraApiFactory { Settings = { ["Library:SyncInterval"] = "00:00:10" } };

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Library:SyncInterval", exception.Message);
    }

    [Fact]
    public async Task HouseholdLanguages_WithATypo_FailAtStartupNamingIt()
    {
        await using var factory = new AkiraApiFactory { Settings = { ["Playback:Languages:Subtitles"] = "nl,dutsch" } };

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Playback:Languages:Subtitles", exception.Message);
        Assert.Contains("dutsch", exception.Message);
    }

    [Fact]
    public async Task ATrustedProxyThatIsNoAddress_FailsAtStartupNamingIt()
    {
        await using var factory = new AkiraApiFactory { Settings = { ["Proxy:TrustedProxies:0"] = "172.18.0.0/16", ["Proxy:TrustedProxies:1"] = "nginx" } };

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Proxy:TrustedProxies", exception.Message);
        Assert.Contains("nginx", exception.Message);
        Assert.DoesNotContain("172.18", exception.Message);
    }

    [Fact]
    public async Task EventRetentionBelowAnHour_FailsAtStartup()
    {
        await using var factory = new AkiraApiFactory { Settings = { ["Events:Retention"] = "00:10:00" } };

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Events:Retention", exception.Message);
    }
}
