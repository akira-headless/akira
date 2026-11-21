namespace Akira.Core.Playback;

/// <summary>
/// Represents what a stopped playback means for the watch state. ResumePositionMs is 0 when it was played to the end
/// or stopped too early to resume.
/// </summary>
public sealed record StopOutcome(bool Played, long ResumePositionMs);
