namespace Akira.Core.Entities;

// A bearer token of a device: its own token, or a profile session on a shared device
public class DeviceToken : BaseEntity
{
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    // Set on a personal device and on a profile session, empty for the base token of a shared device
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public required string TokenHash { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
