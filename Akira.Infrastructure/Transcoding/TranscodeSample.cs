namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Represents how fast ffmpeg encodes against realtime and in frames per second.
/// </summary>
public sealed record TranscodeSample(DateTimeOffset At, double Speed, double Fps);
