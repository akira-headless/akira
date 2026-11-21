namespace Akira.Core.Playback;

/// <summary>
/// Decides what a stopped playback means for the user's watch state.
/// </summary>
public static class WatchProgress
{
    public static StopOutcome Stop(long positionMs, long? durationMs, double playedThreshold, TimeSpan minimumResume, long resumeBeforeMs)
    {
        if (durationMs > 0 && positionMs >= durationMs * playedThreshold)
        {
            return new StopOutcome(true, 0);
        }

        // Stopping in the first minute is trying something, not watching it, and a player that failed before its first
        // frame stops at 0 too. So where the user was stays where it was.
        var resumeMs = positionMs < minimumResume.TotalMilliseconds ? resumeBeforeMs : positionMs;
        return new StopOutcome(false, resumeMs);
    }
}
