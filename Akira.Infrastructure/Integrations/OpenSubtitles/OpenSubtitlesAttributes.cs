using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.OpenSubtitles;

/// <summary>
/// Represents the attributes of a subtitle in an OpenSubtitles search. MoviehashMatch is true when it was made for the
/// file with the hash searched for.
/// </summary>
internal sealed record OpenSubtitlesAttributes(
    string? Language,
    [property: JsonPropertyName("download_count")] int DownloadCount,
    [property: JsonPropertyName("hearing_impaired")] bool HearingImpaired,
    [property: JsonPropertyName("foreign_parts_only")] bool ForeignPartsOnly,
    [property: JsonPropertyName("moviehash_match")] bool MoviehashMatch,
    string? Release,
    List<OpenSubtitlesFile>? Files);
