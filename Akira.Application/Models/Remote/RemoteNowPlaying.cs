using Akira.Server.Models.Playback;

namespace Akira.Server.Models.Remote;

/// <summary>
/// Represents what a device that can be controlled plays. While it plays, PositionMs moves on by the
/// time since the last report. Markers are the plan's, so a remote shows up next when the player does.
/// </summary>
public sealed record RemoteNowPlaying(Guid SessionId, PlanItem Item, long PositionMs, long? DurationMs, bool IsPaused,
    IReadOnlyList<PlanMarker> Markers);
