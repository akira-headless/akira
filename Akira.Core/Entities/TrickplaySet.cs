using Akira.Core.Enum;

namespace Akira.Core.Entities;

// The thumbnail sheets of one file. No row yet means they were never made; a row with an older Version or other
// settings is made again.
public class TrickplaySet : BaseEntity
{
    public Guid MediaFileId { get; set; }
    public TrickplayStatus Status { get; set; }
    public int Version { get; set; }

    public int IntervalMs { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int TileColumns { get; set; }
    public int TileRows { get; set; }
    public int ThumbnailCount { get; set; }

    public string? Error { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
}
