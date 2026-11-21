using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class Device : BaseEntity
{
    public required string Name { get; set; }
    public required string Client { get; set; }
    public string? ClientVersion { get; set; }
    public string? Platform { get; set; }
    public DeviceKind Kind { get; set; }

    // The owner of a personal device. A shared device has none, its users pick a profile.
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public List<DeviceToken> Tokens { get; set; } = [];
}
