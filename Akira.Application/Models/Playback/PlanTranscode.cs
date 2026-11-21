namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents what the hls source changes, null when the version goes out as it is. Video is copy or
/// h264, audio copy, aac or ac3. BurnedSubtitleStreamId is the subtitle drawn into the picture, which
/// the client then does not show itself.
/// </summary>
public sealed record PlanTranscode(string Video, string Audio, int? Height, bool ToneMap, IReadOnlyList<string> Reasons, string? BurnedSubtitleStreamId);
