using Akira.Core.Media;
using Xunit;

namespace Akira.Core.Tests.Media;

public class SubtitleFilesTests
{
    private const string Video = "28 Weeks Later 2007 Bluray-1080p.mkv";

    [Theory]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.eng.srt", "eng", false, false, null)]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.eng.0.srt", "eng", false, false, null)]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.nld.srt", "dut", false, false, null)]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.en.forced.srt", "eng", true, false, null)]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.nl.sdh.ass", "dut", false, true, null)]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.en.hi.srt", "eng", false, true, null)]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.hi.srt", "hin", false, false, null)]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.srt", null, false, false, null)]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.nl.lingarr.srt", "dut", false, false, "lingarr")]
    [InlineData("28 WEEKS LATER 2007 BLURAY-1080P.ENG.SRT", "eng", false, false, null)]
    public void Parse_ReadsLanguageAndFlags(string fileName, string? language, bool forced, bool hearingImpaired, string? title)
    {
        var parsed = SubtitleFiles.Parse(Video, fileName);

        Assert.NotNull(parsed);
        Assert.Equal((language, forced, hearingImpaired, title),
            (parsed.Language, parsed.IsForced, parsed.IsHearingImpaired, parsed.Title));
    }

    [Theory]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.mkv")]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.nfo")]
    [InlineData("28 Weeks Later 2007 Bluray-1080p.en.sub")]
    [InlineData("28 Weeks Later 2007 Bluray-1080p-sample.en.srt")]
    [InlineData("28 Days Later 2002 Bluray-1080p.en.srt")]
    public void Parse_IgnoresFilesThatAreNotItsSubtitles(string fileName)
    {
        Assert.Null(SubtitleFiles.Parse(Video, fileName));
    }

    [Theory]
    [InlineData("subrip", true)]
    [InlineData("ass", true)]
    [InlineData("mov_text", true)]
    [InlineData("webvtt", true)]
    [InlineData("hdmv_pgs_subtitle", false)]
    [InlineData("dvd_subtitle", false)]
    [InlineData(null, false)]
    public void IsTextBased(string? codec, bool expected)
    {
        Assert.Equal(expected, SubtitleFiles.IsTextBased(codec));
    }
}
