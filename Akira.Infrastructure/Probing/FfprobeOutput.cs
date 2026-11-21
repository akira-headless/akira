namespace Akira.Infrastructure.Probing;

/// <summary>
/// Represents the JSON ffprobe writes for one file, with its streams, container and chapters.
/// </summary>
public sealed record FfprobeOutput(
    List<FfprobeStream>? Streams,
    FfprobeFormat? Format,
    List<FfprobeChapter>? Chapters);
