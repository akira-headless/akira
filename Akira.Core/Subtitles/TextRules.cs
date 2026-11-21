namespace Akira.Core.Subtitles;

/// <summary>
/// Represents how the text of one file is read. SRT takes HTML tags and literal braces, ASS treats a block without a
/// backslash as a comment. PlayResY is the height \pos and \move are measured in.
/// </summary>
internal sealed record TextRules(bool Ass, bool SoftBreaks, double PlayResY)
{
    public static readonly TextRules Srt = new(false, true, 288);
}
