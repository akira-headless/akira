namespace Akira.Server.Models.Subtitles;

/// <summary>
/// Represents what can be done for a subtitle language of a version. Problems lists the sources that failed.
/// </summary>
public sealed record SubtitleOptionsResponse(
    Guid ItemId,
    Guid FileId,
    string Language,
    IReadOnlyList<ExistingSubtitle> Existing,
    IReadOnlyList<DownloadOption> Downloads,
    IReadOnlyList<TranslationOption> Translations,
    IReadOnlyList<string> Problems);
