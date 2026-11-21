using System.Text.RegularExpressions;

namespace Akira.Core.Media;

/// <summary>
/// Reads track flags from a track title, for files whose muxer never set the disposition.
/// </summary>
public static partial class TrackTitles
{
    public static bool IsCommentary(string? title) => title is not null && Commentary().IsMatch(title);

    // Case-sensitive on purpose: SDH, CC and HI are written in capitals, "Hi" is a word
    public static bool IsHearingImpaired(string? title) => title is not null && HearingImpaired().IsMatch(title);

    public static bool IsForced(string? title) => title is not null && Forced().IsMatch(title);

    public static bool IsDescriptive(string? title) => title is not null && Descriptive().IsMatch(title);

    [GeneratedRegex(@"commentary", RegexOptions.IgnoreCase)]
    private static partial Regex Commentary();

    [GeneratedRegex(@"\b(SDH|CC|HI)\b|(?i:hearing[\s-]*impaired)", RegexOptions.None)]
    private static partial Regex HearingImpaired();

    [GeneratedRegex(@"\bforced\b", RegexOptions.IgnoreCase)]
    private static partial Regex Forced();

    [GeneratedRegex(@"audio[\s-]*description|\bdescriptive\b|described[\s-]*video", RegexOptions.IgnoreCase)]
    private static partial Regex Descriptive();
}
