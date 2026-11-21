namespace Akira.Server.Models.Subtitles;

/// <summary>
/// Represents a subtitle track a translator can translate from. Reason says why not when Translatable is false.
/// </summary>
public sealed record TranslationOption(
    string OptionId,
    string Provider,
    string ProviderName,
    string SourceStreamId,
    string SourceLanguage,
    string? SourceTitle,
    bool External,
    bool Translatable,
    string? Reason);
