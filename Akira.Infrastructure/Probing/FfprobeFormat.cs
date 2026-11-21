namespace Akira.Infrastructure.Probing;

/// <summary>
/// Represents the container of a file as ffprobe reports it. Duration and StartTime are seconds, written as text.
/// </summary>
public sealed record FfprobeFormat(string? FormatName, string? Duration, string? StartTime = null);
