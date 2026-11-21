using System.Numerics;

namespace Akira.Core.Playback;

/// <summary>
/// Finds the stretch two chromaprint fingerprints share, the way an intro or end credits repeat across the episodes of a season.
/// </summary>
public static class AudioMatch
{
    // Chromaprint hops 4096/3 samples at 11025 Hz per point
    public const double SecondsPerPoint = 4096.0 / 3 / 11025;

    /// <summary>
    /// Returns the longest shared stretch between minLength and maxLength, or null when the two have none.
    /// </summary>
    public static SharedAudio? Longest(uint[] a, uint[] b, AudioMatchOptions options)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return null;
        }

        SharedAudio? best = null;
        foreach (var shift in CandidateShifts(a, b, options.CandidateShifts, options.MaxBitErrors))
        {
            var found = LongestAt(a, b, shift, options);
            if (found is not null && (best is null || found.Length > best.Length))
            {
                best = found;
            }
        }

        return best;
    }

    /// <summary>
    /// Finds the shared stretch of every file of a season: each is compared with files of other episodes, nearest first, and keeps
    /// the longest stretch any comparison found. Files of the same episode, its versions, share everything and are never compared.
    /// </summary>
    public static IReadOnlyDictionary<T, SharedAudio> InSeason<T>(IReadOnlyList<(T Key, int Episode, uint[] Points)> files, AudioMatchOptions options,
        int maxComparisons = 4) where T : notnull
    {
        var results = new Dictionary<T, SharedAudio>();
        for (var index = 0; index < files.Count; index++)
        {
            // Neighbours first: the episode before and after share the intro most reliably, a season can change it halfway
            var episode = files[index].Episode;
            var others = Enumerable.Range(0, files.Count)
                .Where(other => files[other].Episode != episode)
                .OrderBy(other => Math.Abs(files[other].Episode - episode))
                .Take(maxComparisons);
            foreach (var other in others)
            {
                var found = Longest(files[index].Points, files[other].Points, options);
                if (found is not null && (!results.TryGetValue(files[index].Key, out var kept) || found.Length > kept.Length))
                {
                    results[files[index].Key] = found;
                }
            }
        }

        return results;
    }

    // Offsets at which many points line up within the bit tolerance. Every offset is tried: an index of identical points misses a
    // re-encode in which most points differ by a bit or two. Ten minutes against ten minutes is a few million popcounts.
    private static IEnumerable<int> CandidateShifts(uint[] a, uint[] b, int count, int maxBitErrors)
    {
        var votes = new List<(int Shift, int Matches)>();
        for (var shift = -(b.Length - 1); shift < a.Length; shift++)
        {
            var first = Math.Max(0, -shift);
            var last = Math.Min(b.Length, a.Length - shift);
            var matches = 0;
            for (var j = first; j < last; j++)
            {
                if (BitOperations.PopCount(a[j + shift] ^ b[j]) <= maxBitErrors)
                {
                    matches++;
                }
            }

            if (matches >= 8)
            {
                votes.Add((shift, matches));
            }
        }

        return votes.OrderByDescending(vote => vote.Matches).Take(count).Select(vote => vote.Shift);
    }

    private static SharedAudio? LongestAt(uint[] a, uint[] b, int shift, AudioMatchOptions options)
    {
        var maxGap = (int)Math.Ceiling(options.MaxGapSeconds / SecondsPerPoint);
        var first = Math.Max(0, -shift);
        var last = Math.Min(b.Length, a.Length - shift);

        int? runStart = null, runEnd = null;
        (int Start, int End)? best = null;
        for (var j = first; j < last; j++)
        {
            if (BitOperations.PopCount(a[j + shift] ^ b[j]) > options.MaxBitErrors)
            {
                continue;
            }

            if (runEnd is { } end && j - end > maxGap)
            {
                Keep(runStart!.Value, end);
                runStart = null;
            }

            runStart ??= j;
            runEnd = j;
        }

        if (runStart is { } openStart)
        {
            Keep(openStart, runEnd!.Value);
        }

        if (best is not { } range)
        {
            return null;
        }

        return new SharedAudio((range.Start + shift) * SecondsPerPoint, (range.End + shift + 1) * SecondsPerPoint,
            range.Start * SecondsPerPoint, (range.End + 1) * SecondsPerPoint);

        void Keep(int start, int end)
        {
            // A stray match next to the shared part would pull its edge out by up to the gap: each end moves in to where
            // most points of the next second match
            var window = (int)Math.Ceiling(1 / SecondsPerPoint);
            while (start < end && Density(start, start + window) < 0.5)
            {
                start++;
            }

            while (end > start && Density(end - window, end) < 0.5)
            {
                end--;
            }

            var seconds = (end - start + 1) * SecondsPerPoint;
            if (seconds >= options.MinLength.TotalSeconds && seconds <= options.MaxLength.TotalSeconds
                && (best is null || end - start > best.Value.End - best.Value.Start))
            {
                best = (start, end);
            }
        }

        double Density(int from, int to)
        {
            from = Math.Max(from, first);
            to = Math.Min(to, last - 1);
            var matched = 0;
            for (var j = from; j <= to; j++)
            {
                if (BitOperations.PopCount(a[j + shift] ^ b[j]) <= options.MaxBitErrors)
                {
                    matched++;
                }
            }

            return to < from ? 0 : matched / (double)(to - from + 1);
        }
    }
}
