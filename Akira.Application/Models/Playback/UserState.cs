namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents a user's watch state of an item.
/// </summary>
public sealed record UserState(long PositionMs, bool IsPlayed, int PlayCount, DateTimeOffset? LastPlayedAt);
