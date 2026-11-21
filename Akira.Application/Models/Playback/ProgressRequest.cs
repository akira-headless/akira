namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the body of a report of the playback position and whether it is paused.
/// </summary>
public sealed record ProgressRequest(long PositionMs, bool IsPaused);
