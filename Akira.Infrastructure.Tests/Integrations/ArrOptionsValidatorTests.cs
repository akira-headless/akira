using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Sync;
using Xunit;

namespace Akira.Infrastructure.Tests.Integrations;

public class ArrOptionsValidatorTests
{
    private readonly ArrOptionsValidator _validator = new();

    [Fact]
    public void NoInstances_OrTwoRadarrsAndASonarr_AreValid()
    {
        Assert.True(_validator.Validate(null, new ArrOptions()).Succeeded);
        Assert.True(_validator.Validate(null, new ArrOptions
        {
            Instances = [Instance("radarr"), Instance("radarr-4k"), Instance("sonarr-anime", ArrKind.Sonarr)]
        }).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Radarr")]
    [InlineData("radarr 4k")]
    [InlineData("-radarr")]
    [InlineData("radarr-")]
    [InlineData("a-key-that-is-much-longer-than-32-chars")]
    [InlineData("tmdb")]
    [InlineData("tvdb")]
    public void InvalidKey_Fails(string key)
    {
        var result = _validator.Validate(null, new ArrOptions { Instances = [Instance(key)] });

        Assert.Contains(result.Failures!, failure => failure.StartsWith("Arr:Instances:0:Key"));
    }

    [Fact]
    public void DuplicateKey_Fails()
    {
        var result = _validator.Validate(null, new ArrOptions { Instances = [Instance("radarr"), Instance("radarr")] });

        Assert.Contains(result.Failures!, failure => failure.StartsWith("Arr:Instances:1:Key") && failure.Contains("another instance"));
    }

    [Fact]
    public void MissingUrlApiKeyAndMapping_AreEachNamed()
    {
        var instance = Instance("radarr");
        instance.Url = "radarr:7878";
        instance.ApiKey = "";
        instance.PathMappings = [new PathMappingOptions { From = "/movies" }];

        var result = _validator.Validate(null, new ArrOptions { Instances = [instance] });

        Assert.Equal(
            ["Arr:Instances:0:Url", "Arr:Instances:0:ApiKey", "Arr:Instances:0:PathMappings:0"],
            result.Failures!.Select(failure => failure.Split(' ')[0]));
    }

    [Fact]
    public void UndefinedKind_Fails()
    {
        var result = _validator.Validate(null, new ArrOptions { Instances = [Instance("radarr", (ArrKind)9)] });

        Assert.Contains(result.Failures!, failure => failure.StartsWith("Arr:Instances:0:Kind"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("too-short", false)]
    [InlineData("0123456789abcdef", true)]
    public void WebhookSecret_IsOptional_ButNotShort(string? secret, bool valid)
    {
        var instance = Instance("radarr");
        instance.WebhookSecret = secret;

        var result = _validator.Validate(null, new ArrOptions { Instances = [instance] });

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            Assert.StartsWith("Arr:Instances:0:WebhookSecret", Assert.Single(result.Failures!));
        }
    }

    private static ArrInstanceOptions Instance(string key, ArrKind kind = ArrKind.Radarr) => new()
    {
        Key = key,
        Kind = kind,
        Url = "http://nas:7878",
        ApiKey = "key"
    };
}
