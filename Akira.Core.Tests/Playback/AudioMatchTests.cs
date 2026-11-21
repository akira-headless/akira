using Akira.Core.Playback;
using Xunit;

namespace Akira.Core.Tests.Playback;

public class AudioMatchTests
{
    private static readonly AudioMatchOptions Intro = new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(150));
    private static readonly int Seconds30 = (int)(30 / AudioMatch.SecondsPerPoint);

    [Fact]
    public void ASharedIntro_IsFoundWhereverItSitsInEachEpisode()
    {
        var intro = Random(1, Seconds30);
        var first = Episode(2, before: 100, intro, after: 2000);
        var second = Episode(3, before: 700, Noisy(intro, 4), after: 2000);

        var shared = AudioMatch.Longest(first, second, Intro)!;

        Assert.Equal(100 * AudioMatch.SecondsPerPoint, shared.StartA, 0.5);
        Assert.Equal(700 * AudioMatch.SecondsPerPoint, shared.StartB, 0.5);
        Assert.Equal(30, shared.Length, 0.5);
    }

    [Fact]
    public void NothingShared_OrSharedForTooShort_IsNoIntro()
    {
        Assert.Null(AudioMatch.Longest(Random(4, 3000), Random(5, 3000), Intro));

        var jingle = Random(6, (int)(8 / AudioMatch.SecondsPerPoint));
        Assert.Null(AudioMatch.Longest(Episode(7, 50, jingle, 2000), Episode(8, 90, jingle, 2000), Intro));
    }

    [Fact]
    public void AShortSilenceInsideTheIntro_DoesNotSplitIt()
    {
        var intro = Random(9, Seconds30);
        var withGap = (uint[])intro.Clone();
        // Two seconds of something else halfway, under the gap the match bridges
        Random(10, 16).CopyTo(withGap, Seconds30 / 2);

        var shared = AudioMatch.Longest(Episode(11, 40, intro, 1500), Episode(12, 40, withGap, 1500), Intro)!;

        Assert.Equal(30, shared.Length, 0.5);
    }

    [Fact]
    public void InASeason_EachEpisodeKeepsTheLongestStretchItShares_VersionsAreNotCompared_AndTheOddOneOutHasNone()
    {
        var intro = Random(13, Seconds30);
        var episode3 = Episode(16, 400, Noisy(intro, 5), 3000);
        var season = new List<(string, int, uint[])>
        {
            ("e1", 1, Episode(14, 0, intro, 3000)),
            ("e2", 2, Episode(15, 250, Noisy(intro, 3), 3000)),
            ("e3", 3, episode3),
            // Another version of episode 3 shares everything with it, which is no intro
            ("e3-4k", 3, Noisy(episode3, 18)),
            ("special", 4, Random(17, 3000))
        };

        var found = AudioMatch.InSeason(season, Intro);

        Assert.Equal(["e1", "e2", "e3", "e3-4k"], found.Keys.Order());
        Assert.Equal(30, found["e3-4k"].Length, 0.5);
        Assert.Equal(400 * AudioMatch.SecondsPerPoint, found["e3"].StartA, 0.5);
    }

    private static uint[] Episode(int seed, int before, uint[] shared, int after) => [.. Random(seed, before), .. shared, .. Random(seed + 1000, after)];

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
