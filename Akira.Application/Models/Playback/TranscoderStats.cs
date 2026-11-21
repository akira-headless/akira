namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents how a transcode keeps up. Speed is against realtime (1 keeps up), EncodedToMs how far into the file the
/// transcode got. History is the last few minutes.
/// </summary>
public sealed record TranscoderStats(string Encoder, bool Running, double? Speed, double? Fps, long? EncodedToMs, IReadOnlyList<TranscoderSample> History);
