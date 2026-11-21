using System.Text;

namespace Akira.Core.Subtitles;

public static class WebVttWriter
{
    /// <summary>
    /// Writes cues as WebVTT in time order, a line at the top with line:0.
    /// </summary>
    public static string Write(IEnumerable<SubtitleCue> cues)
    {
        var builder = new StringBuilder("WEBVTT\n");
        foreach (var cue in Distinct(cues))
        {
            builder.Append('\n').Append(Stamp(cue.StartMs)).Append(" --> ").Append(Stamp(cue.EndMs));
            if (cue.Top)
            {
                builder.Append(" line:0");
            }

            builder.Append('\n').Append(cue.Text).Append('\n');
        }

        return builder.ToString();
    }

    // Fansub files stack the same line in several layers (shadow, glow, the text itself), which a player would show
    // once per layer
    internal static IEnumerable<SubtitleCue> Distinct(IEnumerable<SubtitleCue> cues) => cues.Distinct().OrderBy(cue => cue.StartMs);

    private static string Stamp(long ms) => $"{ms / 3_600_000:00}:{ms / 60_000 % 60:00}:{ms / 1000 % 60:00}.{ms % 1000:000}";
}
