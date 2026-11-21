namespace Akira.Core.Media;

/// <summary>
/// Represents what a subtitle file name like Movie.en.forced.srt says. Title is the words left over, null when none
/// are.
/// </summary>
public sealed record ExternalSubtitleName(
    string? Language,
    bool IsForced,
    bool IsHearingImpaired,
    bool IsDefault,
    string? Title);
