namespace Akira.Core.Entities;

public class WatchState : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    // Not a foreign key: the item can be removed and added again, the user's history stays
    public required string IdentityKey { get; set; }

    public long PositionMs { get; set; }
    public bool IsPlayed { get; set; }
    public int PlayCount { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
}
