namespace Akira.Core.Entities;

public class ApiKey : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public required string Name { get; set; }
    public required string KeyHash { get; set; }

    // The start of the key, so a user can tell their keys apart without the key being stored
    public required string Prefix { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
