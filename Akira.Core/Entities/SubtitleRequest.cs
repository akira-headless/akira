using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class SubtitleRequest : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid MediaItemId { get; set; }
    public Guid MediaFileId { get; set; }

    // ISO 639-2/B, like every language this server stores
    public required string Language { get; set; }
    public SubtitleRequestKind Kind { get; set; }
    public required string ProviderKey { get; set; }

    // Download: the provider's file id. Translate: the track translated from.
    public string? SourceReference { get; set; }
    public string? ExternalJobId { get; set; }

    public SubtitleRequestStatus Status { get; set; }
    public int Progress { get; set; }
    public string? ResultStreamId { get; set; }
    public string? ResultPath { get; set; }
    public string? Error { get; set; }

    // A file this server wrote only for the translator to read, removed again afterwards
    public string? TemporaryPath { get; set; }

    // Set when a playing client asked, so the finished track can be loaded into that playback
    public Guid? SessionId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
