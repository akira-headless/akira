using Akira.Core.Enum;

namespace Akira.Server.Models.Files;

/// <summary>
/// Represents a version of an item, the file one instance has, with its tracks and chapters.
/// </summary>
public sealed record FileDetail(
    Guid Id,
    Guid ItemId,
    string Instance,
    string InstanceName,
    long SizeBytes,
    string? Container,
    long? DurationMs,
    ProbeStatus ProbeStatus,
    string? ProbeError,
    DateTimeOffset? ProbedAt,
    IReadOnlyList<StreamSummary> Streams,
    IReadOnlyList<ChapterSummary> Chapters);
