namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the body of a report that the user changed audio or subtitles. Send SubtitleStreamId or
/// SubtitlesOff, not both.
/// </summary>
public sealed record TracksRequest(string? AudioStreamId, string? SubtitleStreamId, bool SubtitlesOff);
