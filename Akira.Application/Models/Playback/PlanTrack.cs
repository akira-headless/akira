using Akira.Core.Enum;

namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents an audio or subtitle track of a version, with what a client finds it by.
/// </summary>
public sealed record PlanTrack(string StreamId, StreamKind Kind, TrackMatch Match);
