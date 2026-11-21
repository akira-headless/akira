using Akira.Core.Playback;

namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents a part a player can offer to skip, an intro, credits, a recap or the preview of the next episode.
/// Source says whether the chapters or an audio fingerprint found it.
/// </summary>
public sealed record PlanMarker(MarkerKind Kind, long StartMs, long EndMs, MarkerSource Source);
