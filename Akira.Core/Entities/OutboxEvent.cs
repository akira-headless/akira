namespace Akira.Core.Entities;

// One stored event, kept for clients that catch up after being offline
public class OutboxEvent
{
    // Not a BaseEntity: the database numbers events, and that number is the cursor a client resumes from
    public long Sequence { get; set; }
    public required string Type { get; set; }
    public int Version { get; set; }
    public required string Payload { get; set; }

    // Set for events only that user receives, such as watch state. Empty for everyone.
    public Guid? UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
