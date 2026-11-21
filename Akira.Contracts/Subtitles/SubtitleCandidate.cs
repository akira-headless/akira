namespace Akira.Contracts.Subtitles;

/// <summary>
/// Represents a subtitle a search found. Reference is what DownloadAsync takes to fetch it, and HashMatch is true when
/// it was made for this exact file.
/// </summary>
public sealed record SubtitleCandidate(
    string Reference,
    string Language,
    string? Release,
    int Downloads,
    bool HearingImpaired,
    bool Forced,
    bool HashMatch,
    string Format);
