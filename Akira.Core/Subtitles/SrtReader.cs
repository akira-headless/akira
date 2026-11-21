using System.Globalization;
using System.Text.RegularExpressions;

namespace Akira.Core.Subtitles;

public static partial class SrtReader
{
    private const int MaxHours = 999;

    /// <summary>
    /// Reads the cues of an SRT file, also when blank lines are missing, the timing uses a period or an arrow like "->",
    /// or a cue ends before it starts.
    /// </summary>
    public static List<SubtitleCue> Read(string text)
    {
        var cues = new List<SubtitleCue>();
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        (long Start, long End)? timing = null;
        var body = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (Timing(lines[i]) is { } next)
            {
                Add();
                timing = next;
                continue;
            }

            // The number of the next cue, also when the blank line before it is missing
            if (timing is null || (Index().IsMatch(lines[i]) && i + 1 < lines.Length && Timing(lines[i + 1]) is not null))
            {
                continue;
            }

            body.Add(lines[i]);
        }

        Add();
        return cues;

        void Add()
        {
            if (timing is { } at)
            {
                var (cueText, top) = CueText.Read(string.Join('\n', body), TextRules.Srt);
                if (cueText.Length > 0)
                {
                    var startMs = Math.Min(at.Start, at.End);
                    var endMs = Math.Max(at.Start, at.End);
                    cues.Add(new SubtitleCue(startMs, endMs, cueText, top ?? false));
                }
            }

            body.Clear();
        }
    }

    private static (long Start, long End)? Timing(string line)
    {
        var match = TimingLine().Match(line);
        if (!match.Success)
        {
            return null;
        }

        if (Milliseconds(match, 1) is not { } start)
        {
            return null;
        }

        if (Milliseconds(match, 5) is not { } end)
        {
            return null;
        }

        return (start, end);
    }

    internal static long? Milliseconds(Match match, int first)
    {
        var minutes = int.Parse(match.Groups[first + 1].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[first + 2].Value, CultureInfo.InvariantCulture);
        if (minutes >= 60 || seconds >= 60)
        {
            return null;
        }

        // The pattern takes hours of any length; more digits than a number holds is not a time
        if (!int.TryParse(match.Groups[first].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hours) || hours > MaxHours)
        {
            return null;
        }

        // ",5" is half a second, not 5 ms
        var fraction = match.Groups[first + 3].Value.PadRight(3, '0')[..3];
        var milliseconds = int.Parse(fraction, CultureInfo.InvariantCulture);
        return (((long)hours * 60 + minutes) * 60 + seconds) * 1000 + milliseconds;
    }

    [GeneratedRegex(@"^\s*(\d+):(\d{1,2}):(\d{1,2})(?:[,.](\d+))?\s*-\s*-?\s*>\s*(\d+):(\d{1,2}):(\d{1,2})(?:[,.](\d+))?")]
    private static partial Regex TimingLine();

    [GeneratedRegex(@"^\s*\d+\s*$")]
    private static partial Regex Index();
}
