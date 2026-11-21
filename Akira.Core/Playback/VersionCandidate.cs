using Akira.Core.Entities;

namespace Akira.Core.Playback;

/// <summary>
/// Represents a version a client could play. InstanceOrder is its instance's place in the configuration, the last tie
/// breaker.
/// </summary>
public sealed record VersionCandidate(MediaFile File, MediaStream? Video, int InstanceOrder);
