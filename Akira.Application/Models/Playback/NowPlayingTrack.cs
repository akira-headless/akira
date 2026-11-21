namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the audio or subtitle track a playback session plays.
/// </summary>
public sealed record NowPlayingTrack(string StreamId, string? Language, string? Codec, int? Channels, string? Title, bool Forced, bool External);
