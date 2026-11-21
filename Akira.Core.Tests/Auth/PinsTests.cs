using Akira.Core.Auth;
using Xunit;

namespace Akira.Core.Tests.Auth;

public class PinsTests
{
    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("١٢٣٤", false)]
    [InlineData(null, false)]
    public void IsValid(string? pin, bool expected) => Assert.Equal(expected, Pins.IsValid(pin));

    [Fact]
    public void Hash_VerifiesTheSamePinOnly()
    {
        var hash = Pins.Hash("2580");

        Assert.True(Pins.Verify("2580", hash));
        Assert.False(Pins.Verify("2581", hash));
        Assert.False(Pins.Verify(null, hash));
        Assert.StartsWith("pbkdf2-sha256$210000$", hash);
    }

    [Fact]
    public void Hash_IsSaltedPerCall()
    {
        Assert.NotEqual(Pins.Hash("2580"), Pins.Hash("2580"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("pbkdf2-sha256$x$y$z")]
    [InlineData("md5$1$AAAA$AAAA")]
    public void Verify_RejectsMalformedHashes(string hash) => Assert.False(Pins.Verify("2580", hash));
}
