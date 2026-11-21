namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the user a playback session plays for.
/// </summary>
public sealed record NowPlayingUser(Guid Id, string Name);
