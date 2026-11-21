namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the audio or subtitle track a playback plan picked. StreamId and Match are null when nothing is picked,
/// the reason says why.
/// </summary>
public sealed record TrackPick(string? StreamId, string Reason, TrackMatch? Match);
