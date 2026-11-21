namespace Akira.Contracts.Subtitles;

/// <summary>
/// Represents a subtitle file to translate. SubtitlePath is a path on this server, a translator that sees the share
/// elsewhere maps it itself.
/// </summary>
public sealed record TranslationJob(string SubtitlePath, string SourceLanguage, string TargetLanguage, string Format, MediaKind Type);
