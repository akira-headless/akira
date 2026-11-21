namespace Akira.Server.Models.Events;

/// <summary>
/// Represents the payload of the event sent when a user's watch state of an item changed. Reason is progress (a pause
/// or resume), stop (the end of a playback), marked (played or unplayed by hand) or imported (from another server).
/// </summary>
public sealed record WatchStatePayload(
    Guid ItemId,
    string IdentityKey,
    long PositionMs,
    bool IsPlayed,
    int PlayCount,
    DateTimeOffset? LastPlayedAt,
    string Reason);
