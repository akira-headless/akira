namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Represents the transcode of one session. Encoder is copy, libx264 or the GPU encoder. EncodedToMs is the end of the
/// last finished segment of the current run, null before its first. History is the last few minutes, oldest first.
/// </summary>
public sealed record TranscodeStatus(string Encoder, bool Running, long? EncodedToMs, IReadOnlyList<TranscodeSample> History);
