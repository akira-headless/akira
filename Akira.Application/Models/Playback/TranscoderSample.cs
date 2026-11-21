namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents one reading of how fast a transcode runs, its speed against realtime and its frames per second.
/// </summary>
public sealed record TranscoderSample(DateTimeOffset At, double Speed, double Fps);
