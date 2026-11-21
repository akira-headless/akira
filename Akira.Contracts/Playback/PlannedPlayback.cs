namespace Akira.Contracts.Playback;

/// <summary>
/// Represents a planned playback and its session, which nobody hears about until IPlaybackAccess.StartAsync. Hls is true
/// when the file does not suit the client and the server transcodes it, so the client plays IMediaAccess.HlsUrl instead
/// of the file. A burned subtitle is drawn into the picture.
/// </summary>
public sealed record PlannedPlayback(
    Guid SessionId,
    Guid ItemId,
    Guid FileId,
    long ResumePositionMs,
    long? DurationMs,
    string? AudioStreamId,
    string? SubtitleStreamId,
    bool Hls,
    string? BurnedSubtitleStreamId,
    IReadOnlyList<string> TranscodeReasons,
    IReadOnlyList<PlaybackMarker> Markers,
    PlaybackTrickplay? Trickplay);
