namespace Akira.Core.Entities;

public class MediaChapter : BaseEntity
{
    public Guid MediaFileId { get; set; }
    public MediaFile MediaFile { get; set; } = null!;

    public int SortOrder { get; set; }
    public string? Title { get; set; }
    public long StartMs { get; set; }
    public long? EndMs { get; set; }
}
