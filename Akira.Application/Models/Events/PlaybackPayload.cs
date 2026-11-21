namespace Akira.Server.Models.Events;

/// <summary>
/// Represents the payload of the event sent when a playback session opened (a plan was made) or ended. Transcoding says
/// the version goes out as HLS.
/// </summary>
public sealed record PlaybackPayload(
    Guid SessionId,
    Guid ItemId,
    string IdentityKey,
    Guid FileId,
    Guid? DeviceId,
    long PositionMs,
    bool Transcoding,
    bool? Played);
