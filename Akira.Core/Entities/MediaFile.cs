using Akira.Core.Enum;

namespace Akira.Core.Entities;

// One version of an item: the file a single Radarr or Sonarr instance holds for it
public class MediaFile : BaseEntity
{
    public Guid MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; } = null!;

    public required string InstanceKey { get; set; }
    public required string Path { get; set; }
    public long SizeBytes { get; set; }
    public string? Container { get; set; }
    public int ArrFileId { get; set; }
    public DateTimeOffset? AddedAt { get; set; }

    public long? DurationMs { get; set; }
    public ProbeStatus ProbeStatus { get; set; } = ProbeStatus.Pending;
    public string? ProbeError { get; set; }
    public DateTimeOffset? ProbedAt { get; set; }
    public int? ProbeVersion { get; set; }

    // What the file looked like on disk when it was probed, to notice it changing in place
    public long? ProbedSizeBytes { get; set; }
    public DateTimeOffset? ProbedModifiedAt { get; set; }

    public List<MediaStream> Streams { get; set; } = [];
    public List<MediaChapter> Chapters { get; set; } = [];
}
