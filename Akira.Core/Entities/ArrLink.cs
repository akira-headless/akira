namespace Akira.Core.Entities;

// An item being present in one Radarr or Sonarr instance
public class ArrLink : BaseEntity
{
    public Guid MediaItemId { get; set; }
    public MediaItem MediaItem { get; set; } = null!;

    public required string InstanceKey { get; set; }

    // The movie, series or episode id in that instance. Seasons have none.
    public int? ArrId { get; set; }
}
