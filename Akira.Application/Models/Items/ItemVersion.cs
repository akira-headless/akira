using Akira.Core.Enum;

namespace Akira.Server.Models.Items;

/// <summary>
/// Represents a version of an item, the file one instance holds, with its video, audio and subtitle tracks.
/// </summary>
public sealed record ItemVersion(
    Guid FileId,
    string Instance,
    string InstanceName,
    long SizeBytes,
    string? Container,
    DateTimeOffset? AddedAt,
    long? DurationMs,
    ProbeStatus ProbeStatus,
    VersionVideo? Video,
    IReadOnlyList<VersionTrack> Audio,
    IReadOnlyList<VersionTrack> Subtitles);
