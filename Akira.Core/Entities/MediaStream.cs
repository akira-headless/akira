using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class MediaStream
{
    // The stable id from StreamIds, never the ffprobe index
    public required string Id { get; set; }
    public Guid MediaFileId { get; set; }
    public MediaFile MediaFile { get; set; } = null!;

    public StreamKind Kind { get; set; }
    public int SortOrder { get; set; }
    public string? Codec { get; set; }
    public string? Profile { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }

    public int? Channels { get; set; }
    public string? ChannelLayout { get; set; }
    public int? BitDepth { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public HdrFormat? HdrFormat { get; set; }
    public double? FrameRate { get; set; }

    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsCommentary { get; set; }
    public bool IsDescriptive { get; set; }

    public bool IsTextBased { get; set; }
    public bool IsExternal { get; set; }
    public string? ExternalPath { get; set; }
    public int? EventCount { get; set; }

    // The ffprobe index, only ever handed to a client as a hint in a playback plan
    public int? IndexHint { get; set; }
}
