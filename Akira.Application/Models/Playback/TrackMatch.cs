namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents what a client finds a track by. Flags holds default, forced, hearingImpaired, commentary or descriptive,
/// and IndexHint is the ffprobe index, only a hint.
/// </summary>
public sealed record TrackMatch(
    string? Language,
    string? Codec,
    int? Channels,
    string? Title,
    IReadOnlyList<TrackFlag> Flags,
    int? IndexHint,
    string? ExternalUrl,
    string? ExternalPath,
    string? WebVttUrl = null);
