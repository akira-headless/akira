namespace Akira.Infrastructure.Probing;

/// <summary>
/// Represents a chapter as ffprobe reports it. StartTime and EndTime are seconds, written as text.
/// </summary>
public sealed record FfprobeChapter(string? StartTime, string? EndTime, Dictionary<string, string>? Tags);
