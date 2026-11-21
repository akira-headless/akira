namespace Akira.Core.Playback;

/// <summary>
/// Represents the audio and subtitle tracks picked for playback.
/// </summary>
public sealed record TrackSelection(TrackChoice Audio, TrackChoice Subtitle);
