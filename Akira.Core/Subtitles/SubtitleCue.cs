namespace Akira.Core.Subtitles;

/// <summary>
/// Represents one subtitle cue. Text is WebVTT cue text: escaped, a line per line, with only the i, b and u tags as
/// markup.
/// </summary>
public sealed record SubtitleCue(long StartMs, long EndMs, string Text, bool Top = false);
