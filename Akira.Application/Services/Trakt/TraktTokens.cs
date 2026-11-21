using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Akira.Core.Entities;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Trakt;

namespace Akira.Server.Services.Trakt;

/// <summary>
/// Reads, stores and refreshes the Trakt tokens of a link.
/// </summary>
public sealed class TraktTokens
{
    private const string Purpose = "trakt-token";

    // Refreshed a while before they run out, so a scrobble never goes out with a token that just expired
    private static readonly TimeSpan RefreshAhead = TimeSpan.FromHours(1);

    // Trakt takes a refresh token once, so the sync and a request never refresh the same link side by side
    private static readonly SemaphoreSlim Refreshing = new(1, 1);

    private readonly IDataProtectionProvider _dataProtection;
    private readonly ITraktClient _trakt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;

    public TraktTokens(
        IDataProtectionProvider dataProtection,
        ITraktClient trakt,
        IServiceScopeFactory scopeFactory,
        TimeProvider time)
    {
        _dataProtection = dataProtection;
        _trakt = trakt;
        _scopeFactory = scopeFactory;
        _time = time;
    }

    public void Store(TraktLink link, TraktToken token)
    {
        var protector = _dataProtection.CreateProtector(Purpose);
        link.ProtectedAccessToken = protector.Protect(token.AccessToken);
        link.ProtectedRefreshToken = protector.Protect(token.RefreshToken);
        link.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(token.CreatedAt).AddSeconds(token.ExpiresIn);
    }

    public string Protect(string token) => _dataProtection.CreateProtector(Purpose).Protect(token);

    /// <summary>
    /// Returns a usable access token, refreshing it first when it is about to expire. New tokens are saved at once.
    /// </summary>
    public async Task<string> AccessTokenAsync(TraktLink link, CancellationToken cancellationToken)
    {
        var protector = _dataProtection.CreateProtector(Purpose);
        if (IsFresh(link.ExpiresAt))
        {
            return protector.Unprotect(link.ProtectedAccessToken);
        }

        await Refreshing.WaitAsync(cancellationToken);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();

            // Whoever held the lock before may have refreshed this link already
            var stored = await dbContext.TraktLinks.AsNoTracking()
                .Where(l => l.Id == link.Id)
                .Select(l => new { l.ProtectedAccessToken, l.ProtectedRefreshToken, l.ExpiresAt })
                .SingleOrDefaultAsync(cancellationToken);
            if (stored is not null && stored.ProtectedRefreshToken != link.ProtectedRefreshToken)
            {
                link.ProtectedAccessToken = stored.ProtectedAccessToken;
                link.ProtectedRefreshToken = stored.ProtectedRefreshToken;
                link.ExpiresAt = stored.ExpiresAt;
                if (IsFresh(link.ExpiresAt))
                {
                    return protector.Unprotect(link.ProtectedAccessToken);
                }
            }

            var token = await _trakt.RefreshAsync(protector.Unprotect(link.ProtectedRefreshToken), cancellationToken);
            Store(link, token);

            // Trakt has replaced the old refresh token by now. Saved here, and whatever happens to the request:
            // tokens lost between this and the caller's save would leave a link only linking again repairs.
            await dbContext.TraktLinks
                .Where(l => l.Id == link.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(l => l.ProtectedAccessToken, link.ProtectedAccessToken)
                    .SetProperty(l => l.ProtectedRefreshToken, link.ProtectedRefreshToken)
                    .SetProperty(l => l.ExpiresAt, link.ExpiresAt), CancellationToken.None);
            return token.AccessToken;
        }
        finally
        {
            Refreshing.Release();
        }
    }

    private bool IsFresh(DateTimeOffset expiresAt) => expiresAt - _time.GetUtcNow() > RefreshAhead;
}
