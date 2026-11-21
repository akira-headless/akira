using System.Text.RegularExpressions;
using Akira.Core.Entities;

namespace Akira.Core.Playback;

/// <summary>
/// Finds the parts of a file a player can offer to skip.
/// </summary>
public static partial class Markers
{
    // Longer than this is not a title sequence, it is a film whose first chapter is called Opening
    private static readonly TimeSpan MaxIntro = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan MaxRecap = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reads markers from chapter titles, the way release groups and anime fansubs name them. Neighbouring chapters of the same kind become one.
    /// </summary>
    public static IReadOnlyList<Marker> FromChapters(IReadOnlyList<MediaChapter> chapters, long? durationMs)
    {
        var ordered = chapters.OrderBy(chapter => chapter.StartMs).ToList();
        var found = new List<Marker>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var chapter = ordered[index];
            var nextStart = index + 1 < ordered.Count ? ordered[index + 1].StartMs : durationMs;
            var end = chapter.EndMs ?? nextStart;
            if (Kind(chapter.Title) is not { } kind || end is not { } endMs)
            {
                continue;
            }

            if (endMs <= chapter.StartMs || !Plausible(kind, chapter.StartMs, endMs, durationMs))
            {
                continue;
            }

            if (found.Count > 0 && found[^1].Kind == kind && found[^1].EndMs >= chapter.StartMs - 1000)
            {
                found[^1] = found[^1] with { EndMs = endMs };
                continue;
            }

            found.Add(new Marker(kind, chapter.StartMs, endMs, MarkerSource.Chapters));
        }

        return found;
    }

    /// <summary>
    /// Adds what comparing the audio found, for every kind the chapters did not name. Chapter names are exact, a fingerprint a match.
    /// A recap only goes in when it ends before the intro.
    /// </summary>
    public static IReadOnlyList<Marker> WithScan(IReadOnlyList<Marker> fromChapters, MarkerScan? scan)
    {
        var markers = fromChapters.ToList();
        if (scan is { IntroStartMs: { } introStart, IntroEndMs: { } introEnd } && markers.All(m => m.Kind != MarkerKind.Intro))
        {
            markers.Add(new Marker(MarkerKind.Intro, introStart, introEnd, MarkerSource.Fingerprint));
        }

        if (scan is { CreditsStartMs: { } creditsStart, CreditsEndMs: { } creditsEnd } && markers.All(m => m.Kind != MarkerKind.Credits))
        {
            markers.Add(new Marker(MarkerKind.Credits, creditsStart, creditsEnd, MarkerSource.Fingerprint));
        }

        // The scan only knows the intro it found itself, the chapters may name an earlier one
        if (scan is { RecapStartMs: { } recapStart, RecapEndMs: { } recapEnd } && markers.All(m => m.Kind != MarkerKind.Recap)
            && markers.All(m => m.Kind != MarkerKind.Intro || m.StartMs >= recapEnd))
        {
            markers.Add(new Marker(MarkerKind.Recap, recapStart, recapEnd, MarkerSource.Fingerprint));
        }

        return markers.OrderBy(m => m.StartMs).ToList();
    }

    public static MarkerKind? Kind(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var text = title.Trim();
        if (Recap().IsMatch(text)) return MarkerKind.Recap;
        if (Intro().IsMatch(text)) return MarkerKind.Intro;
        if (Credits().IsMatch(text)) return MarkerKind.Credits;
        if (Preview().IsMatch(text)) return MarkerKind.Preview;
        return null;
    }

    private static bool Plausible(MarkerKind kind, long startMs, long endMs, long? durationMs)
    {
        var length = TimeSpan.FromMilliseconds(endMs - startMs);
        var half = durationMs / 2;
        return kind switch
        {
            MarkerKind.Intro => length <= MaxIntro && (half is null || startMs < half),
            MarkerKind.Recap => length <= MaxRecap && (half is null || startMs < half),
            // Credits and a preview of the next episode come at the end
            _ => half is null || startMs >= half
        };
    }

    [GeneratedRegex(@"^(recap|previously( on\b.*)?|the story so far)$", RegexOptions.IgnoreCase)]
    private static partial Regex Recap();

    [GeneratedRegex(@"^(intro(duction)?|opening( (credits|theme|song|titles?|sequence))?|op( ?\d+)?|title (sequence|card)|main titles?|theme song)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Intro();

    [GeneratedRegex(@"^(credits|end(ing)?( (credits|theme|song|titles?))?|closing( credits)?|ed( ?\d+)?|outro)$", RegexOptions.IgnoreCase)]
    private static partial Regex Credits();

    [GeneratedRegex(@"^(preview|next episode( preview)?|next time|next ep(isode)? preview)$", RegexOptions.IgnoreCase)]
    private static partial Regex Preview();
}
