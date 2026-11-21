using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Akira.Core.Subtitles;

internal static partial class CueText
{
    /// <summary>
    /// Converts the text of one SRT cue or ASS event to WebVTT cue text, and tells whether its tags put it at the top or
    /// the bottom, or null when they do not say.
    /// </summary>
    public static (string Text, bool? Top) Read(string raw, TextRules rules)
    {
        var state = new State(rules);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '{' && raw.IndexOf('}', i) is var close and > 0)
            {
                var block = raw[(i + 1)..close];
                if (block.Contains('\\'))
                {
                    state.Apply(block);
                }
                else if (!rules.Ass)
                {
                    state.Append(raw[i..(close + 1)]);
                }

                i = close;
            }
            // Only what reads as a tag: "3 < 5 and 7 > 2" keeps its middle
            else if (c == '<' && !rules.Ass && raw.IndexOf('>', i) is var end and > 0 && Tag().IsMatch(raw.AsSpan((i + 1)..end)))
            {
                state.Html(raw[(i + 1)..end]);
                i = end;
            }
            else if (c == '\\' && i + 1 < raw.Length && raw[i + 1] is 'N' or 'n' or 'h')
            {
                var lineBreak = raw[i + 1] switch
                {
                    'N' => "\n",
                    'n' => rules.SoftBreaks ? "\n" : " ",
                    _ => " "
                };
                state.Append(lineBreak);
                i++;
            }
            else
            {
                state.Append(c.ToString());
            }
        }

        return (Lines(state.Finish()), state.Top);
    }

    // Collapses spaces and drops lines left empty or holding only the points of a drawing whose \p tag was malformed;
    // markup on a dropped line moves to the line before, so every tag stays closed
    private static string Lines(string text)
    {
        var kept = new List<string>();
        var carried = string.Empty;
        foreach (var line in text.Split('\n'))
        {
            var collapsed = Spaces().Replace(line, " ").Trim(' ', '\t');
            var plain = Markup().Replace(collapsed, string.Empty).Trim();
            if (plain.Length == 0 || Drawing().IsMatch(plain))
            {
                var markup = string.Concat(Markup().Matches(collapsed).Select(match => match.Value));
                if (kept.Count > 0)
                {
                    kept[^1] += markup;
                }
                else
                {
                    carried += markup;
                }

                continue;
            }

            kept.Add(carried + collapsed);
            carried = string.Empty;
        }

        return string.Join('\n', kept);
    }

    private sealed class State
    {
        private readonly TextRules _rules;
        private readonly StringBuilder _text = new();
        private readonly List<char> _open = [];
        private bool _drawing;
        private int? _alignment;
        private double? _y;

        public State(TextRules rules)
        {
            _rules = rules;
        }

        public bool? Top
        {
            get
            {
                if (_y is { } y)
                {
                    return y < _rules.PlayResY / 2;
                }

                if (_alignment is { } alignment)
                {
                    return alignment is >= 7 and <= 9;
                }

                return null;
            }
        }

        public void Append(string text)
        {
            if (_drawing)
            {
                return;
            }

            foreach (var c in text)
            {
                var escaped = c switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    _ => c.ToString()
                };
                _text.Append(escaped);
            }
        }

        // Splitting on the backslash keeps animated tags such as \t(\i1) from counting: their part ends in a parenthesis
        public void Apply(string block)
        {
            foreach (var tag in block.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Number().Match(tag) is not { Success: true } match)
                {
                    if (Position().Match(tag) is { Success: true } position
                        && double.TryParse(position.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    {
                        _y = y;
                    }
                    else if (tag == "r")
                    {
                        Style('i', false);
                        Style('b', false);
                        Style('u', false);
                    }

                    continue;
                }

                // A number of twenty digits is no value of any tag, and must not fail the whole track
                if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    continue;
                }

                switch (match.Groups[1].Value)
                {
                    case "an":
                        _alignment = value;
                        break;
                    case "a":
                        _alignment = Alignment.FromSsa(value);
                        break;
                    case "i":
                        Style('i', value != 0);
                        break;
                    case "b":
                        // A weight counts as bold from semibold on
                        Style('b', value == 1 || value >= 600);
                        break;
                    case "u":
                        Style('u', value != 0);
                        break;
                    case "p":
                        _drawing = value > 0;
                        break;
                }
            }
        }

        public void Html(string tag)
        {
            var name = tag.Trim().ToLowerInvariant();
            if (name is "i" or "b" or "u")
            {
                Style(name[0], true);
            }
            else if (name is "/i" or "/b" or "/u")
            {
                Style(name[1], false);
            }
        }

        public string Finish()
        {
            for (var index = _open.Count - 1; index >= 0; index--)
            {
                _text.Append("</").Append(_open[index]).Append('>');
            }

            return _text.ToString();
        }

        private void Style(char tag, bool on)
        {
            if (on == _open.Contains(tag))
            {
                return;
            }

            if (on)
            {
                _open.Add(tag);
                _text.Append('<').Append(tag).Append('>');
            }
            else
            {
                _open.Remove(tag);
                _text.Append("</").Append(tag).Append('>');
            }
        }
    }

    [GeneratedRegex(@"^/?[A-Za-z][A-Za-z0-9]*(\s[^<]*)?/?$")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^(an|a|i|b|u|p)(\d+)$")]
    private static partial Regex Number();

    [GeneratedRegex(@"^(?:pos|move)\(\s*[-\d.]+\s*,\s*([-\d.]+)")]
    private static partial Regex Position();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex("</?[ibu]>")]
    private static partial Regex Markup();

    [GeneratedRegex(@"^m\s+-?\d+(\.\d+)?\s+-?\d+(\.\d+)?(\s+([mnlbspc]|-?\d+(\.\d+)?))*$")]
    private static partial Regex Drawing();
}
