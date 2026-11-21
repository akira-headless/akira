using Xunit;

namespace Akira.Core.Tests;

public class ServerVersionTests
{
    [Theory]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData("1.4.0+3f2a9c1d", "1.4.0")]
    [InlineData("0.0.0-dev+3f2a9c1d", "0.0.0-dev")]
    [InlineData("", "0.0.0")]
    [InlineData(null, "0.0.0")]
    public void Parse_StripsBuildMetadata(string? informationalVersion, string expected)
    {
        Assert.Equal(expected, ServerVersion.Parse(informationalVersion));
    }

    [Fact]
    public void Number_HasNoBuildMetadata()
    {
        Assert.DoesNotContain('+', ServerVersion.Number);
    }
}
