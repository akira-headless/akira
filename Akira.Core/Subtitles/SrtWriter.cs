using System.Text;

namespace Akira.Core.Subtitles;

public static class SrtWriter
{
    /// <summary>
    /// Writes cues as SRT in time order, a line at the top with {\an8}, which ffmpeg's subtitles filter reads.
    /// </summary>
    public static string Write(IEnumerable<SubtitleCue> cues)
    {
        var builder = new StringBuilder();
        var index = 0;
        foreach (var cue in WebVttWriter.Distinct(cues))
        {
            var position = cue.Top ? @"{\an8}" : string.Empty;
            var text = cue.Text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
            builder.Append(++index).Append('\n')
                .Append(Stamp(cue.StartMs)).Append(" --> ").Append(Stamp(cue.EndMs)).Append('\n')
                .Append(position)
                .Append(text).Append("\n\n");
        }

        return builder.ToString();
    }

    private static string Stamp(long ms) => $"{ms / 3_600_000:00}:{ms / 60_000 % 60:00}:{ms / 1000 % 60:00},{ms % 1000:000}";
}
