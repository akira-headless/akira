namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents a playback session that is running now. Delivery is direct (the client opens the file,
/// by path or over HTTP) or hls (the server segments it).
/// </summary>
public sealed record NowPlaying(
    Guid SessionId,
    NowPlayingUser User,
    NowPlayingDevice? Device,
    PlanItem Item,
    long PositionMs,
    long? DurationMs,
    bool IsPaused,
    DateTimeOffset StartedAt,
    DateTimeOffset LastProgressAt,
    PlaybackDelivery Delivery,
    PlanTranscode? Transcode,
    NowPlayingVersion Version,
    NowPlayingTrack? Audio,
    NowPlayingTrack? Subtitle,
    TranscoderStats? Transcoder);
