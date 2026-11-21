using System.Globalization;
using System.Text.RegularExpressions;

namespace Akira.Core.Subtitles;

public static partial class AssReader
{
    private static readonly string[] DefaultEvents = ["Layer", "Start", "End", "Style", "Name", "MarginL", "MarginR", "MarginV", "Effect", "Text"];
    private static readonly string[] DefaultStyles =
    [
        "Name", "Fontname", "Fontsize", "PrimaryColour", "SecondaryColour", "OutlineColour", "BackColour", "Bold", "Italic", "Underline",
        "StrikeOut", "ScaleX", "ScaleY", "Spacing", "Angle", "BorderStyle", "Outline", "Shadow", "Alignment", "MarginL", "MarginR", "MarginV",
        "Encoding"
    ];

    /// <summary>
    /// Reads the dialogue of an ASS or SSA file, with the columns its Format lines declare, and which lines its styles and
    /// tags put at the top.
    /// </summary>
    public static List<SubtitleCue> Read(string text)
    {
        var section = string.Empty;
        var wrapStyle = 0;
        var playResY = 288.0;
        var legacy = false;
        var styleFormat = DefaultStyles;
        var eventFormat = DefaultEvents;
        var topStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dialogues = new List<string>();
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                section = line.ToLowerInvariant();
                legacy |= section == "[v4 styles]";
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].TrimStart();
            switch (section)
            {
                case "[script info]" when key.Equals("WrapStyle", StringComparison.OrdinalIgnoreCase):
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out wrapStyle);
                    break;
                case "[script info]" when key.Equals("PlayResY", StringComparison.OrdinalIgnoreCase):
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) && height > 0)
                    {
                        playResY = height;
                    }

                    break;
                case "[v4+ styles]" or "[v4 styles]" when key == "Format":
                    styleFormat = Columns(value);
                    break;
                case "[v4+ styles]" or "[v4 styles]" when key == "Style":
                    var style = Fields(value, styleFormat);
                    if (TopStyleName(style, legacy) is { } name)
                    {
                        topStyles.Add(name.Trim());
                    }

                    break;
                case "[events]" when key == "Format":
                    eventFormat = Columns(value);
                    break;
                case "[events]" when key == "Dialogue":
                    dialogues.Add(value);
                    break;
            }
        }

        var rules = new TextRules(true, wrapStyle == 2, playResY);
        var withoutLayer = eventFormat.Where(column => !column.Equals("Layer", StringComparison.OrdinalIgnoreCase)).ToArray();
        var cues = new List<SubtitleCue>();
        foreach (var dialogue in dialogues)
        {
            // Older Aegisub files leave out the Layer their Format line declares
            var fields = Timed(Fields(dialogue, eventFormat)) ?? Timed(Fields(dialogue, withoutLayer));
            if (fields is null || !fields.TryGetValue("Text", out var raw))
            {
                continue;
            }

            var (cueText, top) = CueText.Read(raw, rules);
            if (cueText.Length > 0)
            {
                var start = Time(fields["Start"])!.Value;
                var end = Time(fields["End"])!.Value;
                var styled = fields.TryGetValue("Style", out var style) && topStyles.Contains(style.Trim());
                var startMs = Math.Min(start, end);
                var endMs = Math.Max(start, end);
                cues.Add(new SubtitleCue(startMs, endMs, cueText, top ?? styled));
            }
        }

        return cues;
    }

    private static string[] Columns(string value) => value.Split(',', StringSplitOptions.TrimEntries);

    private static string? TopStyleName(Dictionary<string, string>? style, bool legacy)
    {
        if (style is null || !style.TryGetValue("Name", out var name) || !style.TryGetValue("Alignment", out var alignment))
        {
            return null;
        }

        if (!int.TryParse(alignment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var keypad = legacy ? Alignment.FromSsa(number) : number;
        return keypad is >= 7 and <= 9 ? name : null;
    }

    // Text is the last column and keeps its commas
    private static Dictionary<string, string>? Fields(string value, string[] format)
    {
        var parts = value.Split(',', format.Length);
        if (parts.Length < format.Length)
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < format.Length; i++)
        {
            fields[format[i]] = parts[i];
        }

        return fields;
    }

    private static Dictionary<string, string>? Timed(Dictionary<string, string>? fields)
    {
        if (fields is null || !fields.TryGetValue("Start", out var start) || !fields.TryGetValue("End", out var end))
        {
            return null;
        }

        if (Time(start) is null || Time(end) is null)
        {
            return null;
        }

        return fields;
    }

    private static long? Time(string value)
    {
        var match = Clock().Match(value);
        return match.Success ? SrtReader.Milliseconds(match, 1) : null;
    }

    [GeneratedRegex(@"^\s*(\d+):(\d{1,2}):(\d{1,2})(?:[.,](\d+))?\s*$")]
    private static partial Regex Clock();
}
