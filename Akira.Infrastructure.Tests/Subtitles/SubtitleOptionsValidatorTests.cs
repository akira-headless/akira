using Akira.Infrastructure.Subtitles;
using Akira.Infrastructure.Sync;
using Xunit;

namespace Akira.Infrastructure.Tests.Subtitles;

public class SubtitleOptionsValidatorTests
{
    private readonly SubtitleOptionsValidator _validator = new();

    [Fact]
    public void NothingConfigured_IsValid()
    {
        Assert.True(_validator.Validate(null, new SubtitleOptions()).Succeeded);
    }

    [Fact]
    public void Lingarr_WithAUrlAndNoKey_IsValid_BecauseLingarrMayRunWithoutAuthentication()
    {
        Assert.True(_validator.Validate(null, new SubtitleOptions { Lingarr = new LingarrOptions { Url = "http://lingarr:9876" } }).Succeeded);
    }

    [Fact]
    public void Lingarr_WithAKeyButNoUrl_NamesTheUrl()
    {
        var result = _validator.Validate(null, new SubtitleOptions { Lingarr = new LingarrOptions { ApiKey = "k" } });

        Assert.Equal(["Subtitles:Lingarr:Url"], result.Failures!.Select(failure => failure.Split(' ')[0]));
    }

    [Fact]
    public void TheFfmpegPathOfBeforeTheServerHadOne_StopsTheServer_AndNamesTheKeyThatReplacedIt()
    {
        var result = _validator.Validate(null, new SubtitleOptions { FfmpegPath = "/opt/ffmpeg/bin/ffmpeg" });

        Assert.Contains("Transcoding:FfmpegPath", Assert.Single(result.Failures!));
    }

    [Fact]
    public void HalfAnAccountOrAHalfMapping_AreNamed()
    {
        var result = _validator.Validate(null, new SubtitleOptions
        {
            OpenSubtitles = new OpenSubtitlesOptions { ApiKey = "k", Username = "rowan" },
            Lingarr = new LingarrOptions { Url = "lingarr", ApiKey = "k", PathMappings = [new PathMappingOptions { From = "/data" }] },
            PollInterval = TimeSpan.Zero
        });

        Assert.Equal(["Subtitles:OpenSubtitles", "Subtitles:Lingarr:Url", "Subtitles:Lingarr:PathMappings:0", "Subtitles:PollInterval"],
            result.Failures!.Select(failure => failure.Split(' ')[0]));
    }
}
