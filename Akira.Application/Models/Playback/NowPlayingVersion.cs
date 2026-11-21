namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the version a playback session plays. Instance is the name of the instance that holds it,
/// and Hdr is hdr10, dolbyvision or hlg, null for SDR or a file not probed yet.
/// </summary>
public sealed record NowPlayingVersion(Guid FileId, string Instance, string? Container, string? VideoCodec, int? Width, int? Height, string? Hdr);
