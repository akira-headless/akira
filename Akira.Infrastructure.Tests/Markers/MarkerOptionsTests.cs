using Akira.Infrastructure.Markers;
using Xunit;

namespace Akira.Infrastructure.Tests.Markers;

public class MarkerOptionsTests
{
    [Fact]
    public void AMinimumLongerThanTheLongestIntro_IsNamed()
    {
        var result = new MarkerOptionsValidator().Validate(null, new MarkerOptions { MinLength = TimeSpan.FromMinutes(5) });

        Assert.Equal(["Markers:MinLength"], result.Failures!.Select(failure => failure.Split(' ')[0]));
    }
}
