using Akira.Core.Library;
using Xunit;

namespace Akira.Core.Tests.Library;

public class BlurHashTests
{
    // The expected hashes come from the reference implementation (the blurhash npm package) for the same pixels
    [Fact]
    public void Encode_MatchesTheReferenceImplementation()
    {
        var gradient = new byte[4 * 3 * 3];
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var at = (y * 4 + x) * 3;
                gradient[at] = (byte)(x * 80);
                gradient[at + 1] = (byte)(y * 120);
                gradient[at + 2] = 200;
            }
        }

        var red = new byte[] { 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0 };

        Assert.Equal("L$H.gY7~FI^m:MNNN]w6dLeXfQeX", BlurHash.Encode(gradient, 4, 3, 4, 3));
        Assert.Equal("00TI:j", BlurHash.Encode(red, 2, 2, 1, 1));
    }
}
