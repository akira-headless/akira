using Akira.Core.Enum;

namespace Akira.Server.Models.Files;

/// <summary>
/// Represents a video, audio or subtitle track of a file. Id is the stable track id, never the ffprobe index, and
/// EventCount is the number of lines of a subtitle track.
/// </summary>
public sealed record StreamSummary(
    string Id,
    StreamKind Kind,
    string? Codec,
    string? Profile,
    string? Language,
    string? Title,
    int? Channels,
    string? ChannelLayout,
    int? BitDepth,
    int? Width,
    int? Height,
    HdrFormat? HdrFormat,
    double? FrameRate,
    bool IsDefault,
    bool IsForced,
    bool IsHearingImpaired,
    bool IsCommentary,
    bool IsDescriptive,
    bool IsTextBased,
    bool IsExternal,
    int? EventCount);
