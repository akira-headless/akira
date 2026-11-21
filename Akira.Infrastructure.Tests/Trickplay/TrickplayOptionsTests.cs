using Akira.Infrastructure.Trickplay;
using Xunit;

namespace Akira.Infrastructure.Tests.Trickplay;

public class TrickplayOptionsTests
{
    [Fact]
    public void AnOddWidthOrAHugeGrid_IsNamed()
    {
        var result = new TrickplayOptionsValidator().Validate(null, new TrickplayOptions { Width = 321, Columns = 50 });

        Assert.Equal(["Trickplay:Width", "Trickplay:Columns"], result.Failures!.Select(failure => failure.Split(' ')[0]));
    }
}
