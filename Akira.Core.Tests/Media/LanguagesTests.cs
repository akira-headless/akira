using Akira.Core.Media;
using Xunit;

namespace Akira.Core.Tests.Media;

public class LanguagesTests
{
    [Theory]
    [InlineData("eng", "eng")]
    [InlineData("ENG", "eng")]
    [InlineData("ger", "ger")]
    [InlineData("deu", "ger")]
    [InlineData("nld", "dut")]
    [InlineData("fra", "fre")]
    [InlineData("zho", "chi")]
    [InlineData("en", "eng")]
    [InlineData("ja", "jpn")]
    [InlineData("pt-BR", "por")]
    [InlineData("en_US", "eng")]
    [InlineData("tlh", "tlh")]
    [InlineData("English", "eng")]
    public void Normalize_ReturnsBibliographicCode(string tag, string expected)
    {
        Assert.Equal(expected, Languages.Normalize(tag));
    }

    [Theory]
    [InlineData("und")]
    [InlineData("zxx")]
    [InlineData("mul")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("xx")]
    public void Normalize_UndeterminedOrUnknown_IsNull(string? tag)
    {
        Assert.Null(Languages.Normalize(tag));
    }

    [Theory]
    [InlineData("eng", "eng")]
    [InlineData("nld", "dut")]
    [InlineData("nl", "dut")]
    [InlineData("dutch", "dut")]
    public void FromToken_AcceptsKnownCodesAndNames(string token, string expected)
    {
        Assert.Equal(expected, Languages.FromToken(token));
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("ass")]
    [InlineData("lingarr")]
    [InlineData("tlh")]
    public void FromToken_RejectsWordsThatOnlyLookLikeCodes(string token)
    {
        Assert.Null(Languages.FromToken(token));
    }
}
