using System.Numerics;

namespace Akira.Core.Playback;

/// <summary>
/// Finds a recap: the start of an episode put together from short clips of the episodes before it.
/// </summary>
public static class RecapMatch
{
    // Where a recap may open plus its longest
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(315);

    public static readonly TimeSpan MinLength = TimeSpan.FromSeconds(10);

    private const int MaxBitErrors = 6;

    // A "Previously on" card may come first, a recap never starts later than this
    private const double MaxLeadSeconds = 15;
    private const double MaxRecapSeconds = 300;

    // Shorter matches are a door slam or a sting any episode has; longer is a shared sequence such as the intro
    private const double MinClipSeconds = 1.5;
    private const double MaxClipSeconds = 30;
    private const double MaxClipGapSeconds = 0.75;

    // A clip from an episode not compared, or under a narrator, leaves a gap between the clips that matched
    private const double MaxGapSeconds = 12;
    private const int MinScenes = 3;
    private const double MinCoverage = 0.4;

    /// <summary>
    /// Returns the recap at the start of an episode, or null when it has none. start holds the points of the start up to where
    /// a recap may end, earlier the whole of each episode before it.
    /// </summary>
    public static AudioSpan? Find(uint[] start, IEnumerable<uint[]> earlier)
    {
        var clips = earlier.SelectMany((points, source) => Clips(start, points).Select(clip => (clip.Start, clip.End, Source: source, clip.Shift)))
            .OrderBy(clip => clip.Start)
            .ToList();

        // What the clips cover, with a clip that matches at neighbouring offsets or in several earlier episodes counted once
        var covered = new List<(int Start, int End)>();
        foreach (var clip in clips)
        {
            if (covered.Count > 0 && clip.Start <= covered[^1].End)
            {
                covered[^1] = (covered[^1].Start, Math.Max(covered[^1].End, clip.End));
                continue;
            }

            covered.Add((clip.Start, clip.End));
        }

        if (covered.Count == 0 || covered[0].Start > Points(MaxLeadSeconds))
        {
            return null;
        }

        var matched = covered[0].End - covered[0].Start;
        var end = covered[0].End;
        foreach (var span in covered.Skip(1))
        {
            if (span.Start - end > Points(MaxGapSeconds))
            {
                break;
            }

            matched += span.End - span.Start;
            end = span.End;
        }

        // Back to back clips cover one stretch, so they are told apart by where they come from: a recap shows several
        // scenes, a cold open sharing a music cue with an earlier episode only the one
        var scenes = clips.Where(clip => clip.Start < end)
            .GroupBy(clip => clip.Source)
            .Sum(source => Clusters(source.Select(clip => clip.Shift)));

        var seconds = end * AudioMatch.SecondsPerPoint;
        if (scenes < MinScenes || seconds < MinLength.TotalSeconds || seconds > MaxRecapSeconds || matched < end * MinCoverage)
        {
            return null;
        }

        // From the very start, so the card that announces it goes too
        return new AudioSpan(0, seconds);
    }

    // Every stretch of a that b also has somewhere, as point ranges of a with an exclusive end and the offset into b. Each offset between the two is
    // followed on its own, since every clip of a recap comes from another place.
    private static List<(int Start, int End, int Shift)> Clips(uint[] a, uint[] b)
    {
        var minLength = Points(MinClipSeconds);
        var maxLength = Points(MaxClipSeconds);
        var maxGap = Points(MaxClipGapSeconds);
        var found = new List<(int, int, int)>();
        for (var shift = minLength - a.Length; shift <= b.Length - minLength; shift++)
        {
            var first = Math.Max(0, -shift);
            var last = Math.Min(a.Length, b.Length - shift);
            int runStart = -1, runEnd = -1, matches = 0;
            for (var i = first; i < last; i++)
            {
                if (BitOperations.PopCount(a[i] ^ b[i + shift]) > MaxBitErrors)
                {
                    continue;
                }

                if (runStart >= 0 && i - runEnd > maxGap)
                {
                    Keep();
                    runStart = -1;
                }

                if (runStart < 0)
                {
                    runStart = i;
                    matches = 0;
                }

                runEnd = i;
                matches++;
            }

            if (runStart >= 0)
            {
                Keep();
            }

            void Keep()
            {
                var length = runEnd - runStart + 1;
                if (length >= minLength && length <= maxLength && matches * 2 >= length && Varied(a, runStart, runEnd + 1))
                {
                    found.Add((runStart, runEnd + 1, shift));
                }
            }
        }

        return found;
    }

    private static int Clusters(IEnumerable<int> shifts)
    {
        var count = 0;
        var last = int.MinValue;
        foreach (var shift in shifts.Order())
        {
            if (last == int.MinValue || shift - last > Points(MaxClipGapSeconds))
            {
                count++;
            }

            last = shift;
        }

        return count;
    }

    // Silence is the same few points everywhere and matches any quiet moment
    private static bool Varied(uint[] points, int start, int end) => points[start..end].Distinct().Count() * 2 >= end - start;

    private static int Points(double seconds) => (int)Math.Ceiling(seconds / AudioMatch.SecondsPerPoint);
}
