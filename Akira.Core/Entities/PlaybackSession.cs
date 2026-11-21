using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class PlaybackSession : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid MediaItemId { get; set; }
    public Guid MediaFileId { get; set; }

    public required string IdentityKey { get; set; }

    // Where a track change is remembered: the series for an episode, the item itself for a movie
    public required string MemoryKey { get; set; }
    public PreferenceScope MemoryScope { get; set; }

    public long PositionMs { get; set; }
    public bool IsPaused { get; set; }
    public string? AudioStreamId { get; set; }
    public string? SubtitleStreamId { get; set; }

    // Set when the version goes out as HLS, StreamDecision.ToToken
    public string? StreamDecision { get; set; }

    public DateTimeOffset LastProgressAt { get; set; }

    // Empty while a bridge's client only asked for a plan: it may never play
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}
