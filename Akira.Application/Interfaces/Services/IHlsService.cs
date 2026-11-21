using System.Security.Claims;
using Akira.Infrastructure.Transcoding;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IHlsService
{
    /// <summary>
    /// Writes the master playlist of a session that goes out as HLS.
    /// </summary>
    Task<ServiceResult<string>> GetMasterPlaylistAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the media playlist, every segment of the version listed up front.
    /// </summary>
    Task<ServiceResult<string>> GetMediaPlaylistAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the path of a finished segment, transcoding it first when needed.
    /// </summary>
    Task<ServiceResult<string>> GetSegmentAsync(ClaimsPrincipal user, Guid sessionId, int index, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves what ffmpeg needs to transcode a session: the file, the tracks, the decision and a burned subtitle.
    /// </summary>
    Task<ServiceResult<TranscodeJob>> GetJobAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken);
}
