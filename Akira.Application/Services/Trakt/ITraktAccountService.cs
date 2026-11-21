using Akira.Server.Models.Trakt;

namespace Akira.Server.Services.Trakt;

public interface ITraktAccountService
{
    Task<TraktStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Starts linking a Trakt account: the user enters the returned code at Trakt's verification url.
    /// </summary>
    Task<ServiceResult<TraktStatus>> StartLinkAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Asks Trakt once whether the user entered the code. Once they did the link is stored and their watched history imported.
    /// </summary>
    Task<ServiceResult<TraktStatus>> PollLinkAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the link at Trakt and forgets it here. Watch state already imported stays.
    /// </summary>
    Task<ServiceResult<bool>> UnlinkAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks played what the user watched according to Trakt. Only adds plays, it never marks anything unplayed.
    /// </summary>
    Task<ServiceResult<TraktImportResult>> ImportAsync(Guid userId, CancellationToken cancellationToken);
}
