namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the body of a request for a playback plan. FileId picks the version, the server chooses one otherwise.
/// </summary>
public sealed record PlaybackPlanRequest(CapabilitiesModel? Capabilities, Guid? FileId);
