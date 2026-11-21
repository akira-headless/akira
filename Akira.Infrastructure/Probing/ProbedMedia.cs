using Akira.Core.Entities;

namespace Akira.Infrastructure.Probing;

/// <summary>
/// Represents what probing one file found. DurationMs is null when ffprobe gave no duration.
/// </summary>
public sealed record ProbedMedia(long? DurationMs, List<MediaStream> Streams, List<MediaChapter> Chapters);
