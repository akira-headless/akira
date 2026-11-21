namespace Akira.Contracts.Library;

/// <summary>
/// Represents the caller's own progress and play state of a movie or episode.
/// </summary>
public sealed record UserData(long PositionMs, bool Played, int PlayCount, DateTimeOffset? LastPlayedAt);
