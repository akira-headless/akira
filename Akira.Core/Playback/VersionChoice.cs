using Akira.Core.Entities;

namespace Akira.Core.Playback;

/// <summary>
/// Represents the version picked for a client and why. IsCompatible is false when the client cannot play its video
/// codec or show its HDR format.
/// </summary>
public sealed record VersionChoice(MediaFile File, string Reason, bool IsCompatible);
