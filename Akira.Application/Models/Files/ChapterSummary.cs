namespace Akira.Server.Models.Files;

/// <summary>
/// Represents a chapter of a file. EndMs is null when the file gives no end.
/// </summary>
public sealed record ChapterSummary(string? Title, long StartMs, long? EndMs);
