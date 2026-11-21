using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class MediaImage : BaseEntity
{
    public const int ArrFileNameLength = 100;

    public Guid MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; } = null!;

    public required string InstanceKey { get; set; }
    public ImageKind Kind { get; set; }

    // The movie or series id the instance keys its artwork cache by
    public int? ArrId { get; set; }
    public string? ArrFileName { get; set; }
    public string? RemoteUrl { get; set; }

    // Clients draw it while the image loads. Made in the background from the small copy; empty when the image could not be
    // read, so it is not tried again until the image changes
    public string? BlurHash { get; set; }
}
