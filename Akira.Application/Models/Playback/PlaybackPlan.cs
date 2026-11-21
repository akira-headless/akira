using Akira.Server.Models.Files;

namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the version, sources and tracks a client plays an item with, in the session it opened.
/// TrickplayUrl is null until the file has trickplay thumbnails.
/// </summary>
public sealed record PlaybackPlan(
    Guid SessionId,
    PlanItem Item,
    PlanVersion Version,
    long ResumePositionMs,
    IReadOnlyList<PlanSource> Sources,
    TrackPick Audio,
    TrackPick Subtitle,
    IReadOnlyList<PlanTrack> Tracks,
    IReadOnlyList<ChapterSummary> Chapters,
    PlanTranscode? Transcode,
    IReadOnlyList<PlanMarker> Markers,
    string? TrickplayUrl);
