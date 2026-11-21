using System.Security.Claims;
using Akira.Server.Models.Playback;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IPlaybackService
{
    /// <summary>
    /// Chooses the version, sources and tracks for playing an item on this client, and makes a session for it that is not
    /// started yet: a client may ask for a plan and never play, so nothing shows the user as watching until StartAsync, the
    /// first progress report, or the first segment or file the client fetches.
    /// </summary>
    Task<ServiceResult<PlaybackPlan>> CreatePlanAsync(ClaimsPrincipal user, Guid itemId, PlaybackPlanRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Says which of these files a plan for these capabilities would send as they are, with the user's track rules, without
    /// making a session.
    /// </summary>
    Task<IReadOnlySet<Guid>> PlaysAsIsAsync(ClaimsPrincipal user, IReadOnlyList<Guid> fileIds, CapabilitiesModel capabilities,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts a session that was only planned.
    /// </summary>
    Task<ServiceResult<bool>> StartAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Starts the session the caller's device planned last for a file, when there is one that has not started: the file url
    /// carries no session.
    /// </summary>
    Task StartFileAsync(ClaimsPrincipal user, Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the position of a session as the user's resume point.
    /// </summary>
    Task<ServiceResult<bool>> ReportProgressAsync(ClaimsPrincipal user, Guid sessionId, ProgressRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Plays other tracks in a session and, with remember, keeps them as the user's pick for the series of an episode or for
    /// the movie itself.
    /// </summary>
    Task<ServiceResult<bool>> ChangeTracksAsync(ClaimsPrincipal user, Guid sessionId, TracksRequest request, bool remember,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends a session and marks the item played past the configured threshold. A session that never started ends without
    /// events and leaves the watch state as it is.
    /// </summary>
    Task<ServiceResult<UserState>> StopAsync(ClaimsPrincipal user, Guid sessionId, StopRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Ends a session whose position nobody knows, or that never started, leaving the user's watch state as it is. Returns
    /// false when the session does not exist or has ended.
    /// </summary>
    Task<bool> EndAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken);
}
