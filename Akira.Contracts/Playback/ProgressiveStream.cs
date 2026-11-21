namespace Akira.Contracts.Playback;

/// <summary>
/// Represents what the progressive url of a file sends: the file as it is when AsIs, which takes byte ranges, otherwise
/// a transcode, which takes a time seek. ContentType is the type it is sent with.
/// </summary>
public sealed record ProgressiveStream(Guid FileId, string ContentType, bool AsIs);
