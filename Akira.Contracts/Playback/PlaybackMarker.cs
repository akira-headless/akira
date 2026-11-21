namespace Akira.Contracts.Playback;

/// <summary>
/// Represents a part of a file a player can offer to skip.
/// </summary>
public sealed record PlaybackMarker(MarkerKind Kind, long StartMs, long EndMs);
