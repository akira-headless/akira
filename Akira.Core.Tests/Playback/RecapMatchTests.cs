using Akira.Core.Playback;
using Xunit;

namespace Akira.Core.Tests.Playback;

public class RecapMatchTests
{
    private static readonly int Minutes45 = Points(45 * 60);

    [Fact]
    public void ClipsOfTheEpisodesBefore_AreARecap_FromTheStartToTheLastClip()
    {
        var previous = Random(1, Minutes45);
        var older = Random(2, Minutes45);
        // A narrator over the first clip leaves most of it unmatched; the last clip is from an episode not compared
        var start = Concat(Random(3, Points(2)), Noisy(Narrated(Slice(previous, 120, 4)), 4), Noisy(Slice(previous, 610, 6), 5), Noisy(Slice(older, 905, 3), 6),
            Noisy(Slice(previous, 1500, 5), 7), Noisy(Slice(previous, 2010, 7), 8), Random(9, Points(4)), Random(10, Points(180)));

        var recap = RecapMatch.Find(start, [previous, older])!;

        Assert.Equal(0, recap.Start);
        Assert.Equal(27, recap.End, 0.5);
    }

    [Fact]
    public void AColdOpen_SharingOneMusicCue_OrClipsLateInIt_IsNoRecap()
    {
        var previous = Random(11, Minutes45);
        var cue = Noisy(Slice(previous, 1800, 20), 12);
        Assert.Null(RecapMatch.Find(Concat(Random(13, Points(5)), cue, Random(14, Points(120))), [previous]));

        // A flashback montage a minute into the cold open
        var montage = Concat(Random(15, Points(60)), Slice(previous, 300, 4), Slice(previous, 900, 4), Slice(previous, 1300, 4), Random(16, Points(60)));
        Assert.Null(RecapMatch.Find(montage, [previous]));

        Assert.Null(RecapMatch.Find(Random(17, Points(240)), [previous]));
    }

    private static int Points(double seconds) => (int)(seconds / AudioMatch.SecondsPerPoint);

    private static uint[] Slice(uint[] points, double fromSeconds, double seconds) => points.AsSpan(Points(fromSeconds), Points(seconds)).ToArray();

    private static uint[] Concat(params uint[][] parts) => parts.SelectMany(part => part).ToArray();

    // Only the last two seconds of the clip are heard clearly
    private static uint[] Narrated(uint[] clip) => [.. Random(18, clip.Length - Points(2)), .. clip[^Points(2)..]];

    private static uint[] Random(int seed, int count)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => (uint)random.NextInt64(0, uint.MaxValue)).ToArray();
    }

    // Another encode of the same audio: a few bits differ in each point
    private static uint[] Noisy(uint[] points, int seed)
    {
        var random = new Random(seed);
        return points.Select(point => point ^ (1u << random.Next(32)) ^ (1u << random.Next(32))).ToArray();
    }
}
