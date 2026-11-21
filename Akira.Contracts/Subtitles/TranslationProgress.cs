namespace Akira.Contracts.Subtitles;

/// <summary>
/// Represents how far a translation is. Progress is 0 to 100, and ResultPath is set once it is done.
/// </summary>
public sealed record TranslationProgress(TranslationState Status, int Progress, string? ResultPath, string? Error);
