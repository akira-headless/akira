using Akira.Core.Enum;
using Akira.Server.Models.Playback;

namespace Akira.Server.Models.Subtitles;

/// <summary>
/// Represents a request for a subtitle a file does not have yet. Progress runs from 0 to 100, and Track
/// is the new track once the request is completed.
/// </summary>
public sealed record SubtitleRequestModel(
    Guid Id,
    Guid ItemId,
    Guid FileId,
    string Language,
    SubtitleRequestKind Kind,
    string Provider,
    SubtitleRequestStatus Status,
    int Progress,
    string? ResultStreamId,
    string? Error,
    Guid? SessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    PlanTrack? Track);
