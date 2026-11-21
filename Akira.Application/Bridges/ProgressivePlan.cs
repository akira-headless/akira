using System.Security.Claims;
using Akira.Infrastructure.Transcoding;

namespace Akira.Server.Bridges;

/// <summary>
/// Represents a planned progressive stream: a transcode with its Job, or a file that goes out as it is, Direct, the
/// viewing the request belongs to.
/// </summary>
public sealed record ProgressivePlan(ClaimsPrincipal User, Guid SessionId, Guid FileId, TranscodeJob? Job, long DurationMs, DirectPlay? Direct = null);
