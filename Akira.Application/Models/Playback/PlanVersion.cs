namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the version a playback plan chose. Reason is requested, only-version or best-match, and
/// IsCompatible is false when the client cannot decode its video or show its HDR.
/// </summary>
public sealed record PlanVersion(Guid FileId, string Instance, string InstanceName, string Reason, bool IsCompatible, long? DurationMs);
