namespace Akira.Infrastructure.Integrations.Trakt;

public interface ITraktClient
{
    bool IsConfigured { get; }

    /// <summary>
    /// Starts the device flow: the user enters the code on Trakt's site while this server polls for the token.
    /// </summary>
    Task<TraktDeviceCode> StartDeviceAsync(CancellationToken cancellationToken);

    Task<TraktPoll> PollDeviceAsync(string deviceCode, CancellationToken cancellationToken);

    Task<TraktToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeAsync(string accessToken, CancellationToken cancellationToken);

    Task<string> GetUsernameAsync(string accessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a scrobble: start, pause or stop, with the progress in percent. Trakt adds a stop past 80% to the history itself.
    /// </summary>
    Task ScrobbleAsync(string accessToken, string action, object body, CancellationToken cancellationToken);

    Task AddHistoryAsync(string accessToken, object body, CancellationToken cancellationToken);

    Task RemoveHistoryAsync(string accessToken, object body, CancellationToken cancellationToken);

    Task<IReadOnlyList<TraktWatchedMovie>> GetWatchedMoviesAsync(string accessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<TraktWatchedShow>> GetWatchedShowsAsync(string accessToken, CancellationToken cancellationToken);
}
