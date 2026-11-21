namespace Akira.Server.Models.Subtitles;

/// <summary>
/// Represents a subtitle a source can download. HashMatch is true when it was made for this exact file.
/// </summary>
public sealed record DownloadOption(
    string OptionId,
    string Provider,
    string ProviderName,
    string? Release,
    int Downloads,
    bool HearingImpaired,
    bool Forced,
    bool HashMatch);
