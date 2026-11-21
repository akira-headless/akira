using Akira.Core.Library;
using Xunit;

namespace Akira.Core.Tests.Library;

public class ArrLanguagesTests
{
    [Theory]
    [InlineData("Japanese", "jpn")]
    [InlineData("English", "eng")]
    [InlineData("German", "ger")]
    [InlineData("Dutch", "dut")]
    [InlineData("Flemish", "dut")]
    [InlineData("Portuguese (Brazil)", "por")]
    [InlineData("Spanish (Latino)", "spa")]
    [InlineData("chinese", "chi")]
    [InlineData(" French ", "fre")]
    public void ToIso6392_ReturnsBibliographicCode(string name, string expected)
    {
        Assert.Equal(expected, ArrLanguages.ToIso6392(name));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Original")]
    [InlineData("Any")]
    [InlineData("")]
    [InlineData(null)]
    public void Placeholders_HaveNoCode(string? name)
    {
        Assert.Null(ArrLanguages.ToIso6392(name));
        Assert.True(ArrLanguages.IsPlaceholder(name));
    }

    [Fact]
    public void UnlistedLanguage_HasNoCodeButIsNotAPlaceholder()
    {
        Assert.Null(ArrLanguages.ToIso6392("Klingon"));
        Assert.False(ArrLanguages.IsPlaceholder("Klingon"));
    }
}
