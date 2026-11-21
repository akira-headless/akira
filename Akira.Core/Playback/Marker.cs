namespace Akira.Core.Playback;

/// <summary>
/// Represents a part of a file such as an intro or the credits, in milliseconds, found in chapters or fingerprints.
/// </summary>
public sealed record Marker(MarkerKind Kind, long StartMs, long EndMs, MarkerSource Source);
