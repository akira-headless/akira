using Akira.Core.Entities;

namespace Akira.Core.Playback;

/// <summary>
/// Represents a track picked for playback and why. Stream is null when none is, and Reason is a code like forced.
/// </summary>
public sealed record TrackChoice(MediaStream? Stream, string Reason);
