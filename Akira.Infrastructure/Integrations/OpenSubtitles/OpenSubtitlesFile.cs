using System.Text.Json.Serialization;

namespace Akira.Infrastructure.Integrations.OpenSubtitles;

/// <summary>
/// Represents a file of a subtitle in an OpenSubtitles search. FileId is what a download asks for.
/// </summary>
internal sealed record OpenSubtitlesFile(
    [property: JsonPropertyName("file_id")] long FileId,
    [property: JsonPropertyName("file_name")] string? FileName);
