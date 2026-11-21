namespace Akira.Core.Entities;

// What comparing a file's audio with the other episodes of its season found. No row means it was not compared yet;
// a row with an older Version is compared again.
public class MarkerScan : BaseEntity
{
    public Guid MediaFileId { get; set; }
    public int Version { get; set; }

    public long? RecapStartMs { get; set; }
    public long? RecapEndMs { get; set; }
    public long? IntroStartMs { get; set; }
    public long? IntroEndMs { get; set; }
    public long? CreditsStartMs { get; set; }
    public long? CreditsEndMs { get; set; }

    public string? Error { get; set; }
    public DateTimeOffset ScannedAt { get; set; }
}
