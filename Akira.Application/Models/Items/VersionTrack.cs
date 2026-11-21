namespace Akira.Server.Models.Items;

/// <summary>
/// Represents an audio or subtitle track of a version. IsExternal is true for a file next to the video.
/// </summary>
public sealed record VersionTrack(
    string Id,
    string? Language,
    string? Codec,
    string? Profile,
    int? Channels,
    string? Title,
    bool IsDefault,
    bool IsForced,
    bool IsHearingImpaired,
    bool IsCommentary,
    bool IsExternal);
