using Akira.Core.Auth;
using Xunit;

namespace Akira.Core.Tests.Auth;

public class PairingCodesTests
{
    [Fact]
    public void New_UsesOnlyUnambiguousCharacters()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => PairingCodes.New()).ToList();

        Assert.All(codes, code =>
        {
            Assert.Matches("^[A-Z0-9]{3}-[A-Z0-9]{3}$", code);
            Assert.DoesNotContain(code, c => c is '0' or 'O' or '1' or 'I' or 'L' or '5' or 'S' or '8' or 'B');
        });
    }

    [Theory]
    [InlineData("k7q-4mx", "K7Q-4MX")]
    [InlineData("K7Q4MX", "K7Q-4MX")]
    [InlineData(" k7q 4mx ", "K7Q-4MX")]
    [InlineData("K7Q-4M", null)]
    [InlineData("K7Q-4M0", null)]
    [InlineData(null, null)]
    public void Normalize(string? input, string? expected) => Assert.Equal(expected, PairingCodes.Normalize(input));
}
