using Akira.Contracts.Accounts;

namespace Akira.Contracts.Playback;

/// <summary>
/// Defines how plugins change what a caller has watched.
/// </summary>
public interface IWatchStateAccess
{
    /// <summary>
    /// Marks a movie or episode, or every episode of a series or season, played or not, and returns false when the item
    /// does not exist.
    /// </summary>
    Task<bool> SetPlayedAsync(Caller caller, Guid itemId, bool played, CancellationToken cancellationToken);
}
