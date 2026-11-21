using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Akira.Core.Entities;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Trakt;
using Akira.Server.Models.Trakt;

namespace Akira.Server.Services.Trakt;

public sealed class TraktAccountService : ITraktAccountService
{
    private const int KeyChunk = 500;

    private readonly AkiraDbContext _dbContext;
    private readonly ITraktClient _trakt;
    private readonly TraktTokens _tokens;
    private readonly TraktPendingLinks _pendingLinks;
    private readonly TimeProvider _time;
    private readonly ILogger<TraktAccountService> _logger;

    public TraktAccountService(
        AkiraDbContext dbContext,
        ITraktClient trakt,
        TraktTokens tokens,
        TraktPendingLinks pendingLinks,
        TimeProvider time,
        ILogger<TraktAccountService> logger)
    {
        _dbContext = dbContext;
        _trakt = trakt;
        _tokens = tokens;
        _pendingLinks = pendingLinks;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TraktStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var link = await _dbContext.TraktLinks.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        var pending = _pendingLinks.Get(userId, _time.GetUtcNow());
        var pendingLink = pending is null
            ? null
            : new TraktPending(pending.UserCode, pending.VerificationUrl, pending.ExpiresAt, pending.IntervalSeconds);
        return new TraktStatus(_trakt.IsConfigured, link is not null, link?.Username, link?.CreatedAt, link?.LastImportAt, link?.LastSyncedAt,
            link?.LastError, pendingLink);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TraktStatus>> StartLinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_trakt.IsConfigured)
        {
            return ServiceResult<TraktStatus>.Fail(ServiceError.Conflict, "Trakt is not configured on this server. An admin sets Trakt:ClientId and ClientSecret.");
        }

        try
        {
            var code = await _trakt.StartDeviceAsync(cancellationToken);
            var expiresAt = _time.GetUtcNow().AddSeconds(code.ExpiresIn);
            var intervalSeconds = Math.Max(code.Interval, 1);
            var pending = new TraktPendingLinks.Pending(code.DeviceCode, code.UserCode, code.VerificationUrl, expiresAt, intervalSeconds);
            _pendingLinks.Set(userId, pending);
        }
        catch (UpstreamException exception)
        {
            return ServiceResult<TraktStatus>.Fail(ServiceError.Unavailable, exception.Message);
        }

        return ServiceResult<TraktStatus>.Ok(await GetStatusAsync(userId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TraktStatus>> PollLinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_pendingLinks.Get(userId, _time.GetUtcNow()) is not { } pending)
        {
            return ServiceResult<TraktStatus>.Fail(ServiceError.Gone, "No link is waiting, or its code expired. Start linking again.");
        }

        TraktPoll poll;
        try
        {
            poll = await _trakt.PollDeviceAsync(pending.DeviceCode, cancellationToken);
        }
        catch (UpstreamException exception)
        {
            return ServiceResult<TraktStatus>.Fail(ServiceError.Unavailable, exception.Message);
        }

        switch (poll.Status)
        {
            case TraktPollStatus.Pending or TraktPollStatus.SlowDown:
                return ServiceResult<TraktStatus>.Ok(await GetStatusAsync(userId, cancellationToken));
            case TraktPollStatus.Denied:
                _pendingLinks.Remove(userId);
                return ServiceResult<TraktStatus>.Fail(ServiceError.Forbidden, "The link was declined on Trakt.");
            case not TraktPollStatus.Approved:
                _pendingLinks.Remove(userId);
                return ServiceResult<TraktStatus>.Fail(ServiceError.Gone, "The code expired or was already used. Start linking again.");
        }

        string username;
        try
        {
            username = await _trakt.GetUsernameAsync(poll.Token!.AccessToken, cancellationToken);
        }
        catch (UpstreamException exception)
        {
            return ServiceResult<TraktStatus>.Fail(ServiceError.Unavailable, exception.Message);
        }

        var link = await _dbContext.TraktLinks.SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        if (link is null)
        {
            link = new TraktLink { UserId = userId, Username = username, ProtectedAccessToken = "", ProtectedRefreshToken = "" };
            _dbContext.TraktLinks.Add(link);
        }

        link.Username = username;
        link.LastError = null;
        _tokens.Store(link, poll.Token!);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _pendingLinks.Remove(userId);
        _logger.LogInformation("User {UserId} linked Trakt account {Username}", userId, username);

        var imported = await ImportAsync(userId, cancellationToken);
        if (!imported.Succeeded)
        {
            _logger.LogWarning("Importing the Trakt history of user {UserId} failed: {Reason}", userId, imported.Detail);
        }

        return ServiceResult<TraktStatus>.Ok(await GetStatusAsync(userId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> UnlinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        _pendingLinks.Remove(userId);
        var link = await _dbContext.TraktLinks.SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        if (link is null)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, "No Trakt account is linked.");
        }

        try
        {
            var accessToken = await _tokens.AccessTokenAsync(link, cancellationToken);
            await _trakt.RevokeAsync(accessToken, cancellationToken);
        }
        catch (Exception exception) when (exception is UpstreamException or CryptographicException)
        {
            // Forgotten here either way; the user can remove the app on Trakt's site too
            _logger.LogWarning("Revoking the Trakt token of user {UserId} failed: {Reason}", userId, exception.Message);
        }

        _dbContext.TraktLinks.Remove(link);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TraktImportResult>> ImportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var link = await _dbContext.TraktLinks.SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        if (link is null)
        {
            return ServiceResult<TraktImportResult>.Fail(ServiceError.NotFound, "No Trakt account is linked.");
        }

        var watched = new Dictionary<string, (int Plays, DateTimeOffset? At)>();
        int movies;
        try
        {
            var accessToken = await _tokens.AccessTokenAsync(link, cancellationToken);
            foreach (var movie in await _trakt.GetWatchedMoviesAsync(accessToken, cancellationToken))
            {
                if (movie.Movie.Ids.Tmdb is { } tmdb)
                {
                    watched[TraktItems.MovieKey(tmdb)] = (movie.Plays, movie.LastWatchedAt);
                }
            }

            movies = watched.Count;
            foreach (var show in await _trakt.GetWatchedShowsAsync(accessToken, cancellationToken))
            {
                if (show.Show.Ids.Tvdb is not { } tvdb)
                {
                    continue;
                }

                foreach (var season in show.Seasons ?? [])
                {
                    foreach (var episode in season.Episodes)
                    {
                        watched[TraktItems.EpisodeKey(tvdb, season.Number, episode.Number)] = (episode.Plays, episode.LastWatchedAt);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is UpstreamException or CryptographicException)
        {
            link.LastError = exception.Message;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<TraktImportResult>.Fail(ServiceError.Unavailable, exception.Message);
        }

        // Watch state is keyed by identity key without a foreign key, so plays of what the library does not have yet count once it arrives
        foreach (var chunk in watched.Keys.Chunk(KeyChunk))
        {
            var states = await _dbContext.WatchStates
                .Where(w => w.UserId == userId && chunk.Contains(w.IdentityKey))
                .ToDictionaryAsync(w => w.IdentityKey, cancellationToken);
            foreach (var key in chunk)
            {
                var (plays, at) = watched[key];
                if (!states.TryGetValue(key, out var state))
                {
                    state = new WatchState { UserId = userId, IdentityKey = key };
                    _dbContext.WatchStates.Add(state);
                }

                state.IsPlayed = true;
                state.PlayCount = Math.Max(state.PlayCount, plays);
                if (at is not null && (state.LastPlayedAt is null || at > state.LastPlayedAt))
                {
                    state.LastPlayedAt = at;
                }
            }
        }

        link.LastImportAt = _time.GetUtcNow();
        link.LastError = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        var episodes = watched.Count - movies;
        _logger.LogInformation("Imported {Movies} movies and {Episodes} episodes from Trakt for user {UserId}", movies, episodes, userId);
        return ServiceResult<TraktImportResult>.Ok(new TraktImportResult(movies, episodes));

    }
}
