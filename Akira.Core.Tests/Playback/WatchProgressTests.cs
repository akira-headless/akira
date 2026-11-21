using Akira.Core.Playback;
using Xunit;

namespace Akira.Core.Tests.Playback;

public class WatchProgressTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Theory]
    [InlineData(5_400_000, 6_000_000, 0, true, 0)]
    [InlineData(3_000_000, 6_000_000, 0, false, 3_000_000)]
    [InlineData(30_000, 6_000_000, 0, false, 0)]
    [InlineData(0, 6_000_000, 4_200_000, false, 4_200_000)]
    [InlineData(120_000, 6_000_000, 4_200_000, false, 120_000)]
    public void Stop(long position, long duration, long resumeBefore, bool played, long resume)
    {
        Assert.Equal(new StopOutcome(played, resume), WatchProgress.Stop(position, duration, 0.9, Minute, resumeBefore));
    }

    [Fact]
    public void UnknownDuration_IsNeverPlayed()
    {
        Assert.Equal(new StopOutcome(false, 9_000_000), WatchProgress.Stop(9_000_000, null, 0.9, Minute, 0));
    }
}
