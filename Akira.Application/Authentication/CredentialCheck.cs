using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Akira.Infrastructure.Data;

namespace Akira.Server.Authentication;

/// <summary>
/// Checks that the credential a long running connection signed in with still exists and has not expired.
/// </summary>
public sealed class CredentialCheck
{
    private readonly AkiraDbContext _dbContext;
    private readonly TimeProvider _time;

    public CredentialCheck(
        AkiraDbContext dbContext,
        TimeProvider time)
    {
        _dbContext = dbContext;
        _time = time;
    }

    public async Task<bool> IsValidAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.GetTokenId() is not { } tokenId)
        {
            return false;
        }

        if (user.FindFirstValue(AkiraClaims.CredentialKind) == AkiraClaims.ApiKeyCredential)
        {
            return await _dbContext.ApiKeys.AnyAsync(key => key.Id == tokenId, cancellationToken);
        }

        var now = _time.GetUtcNow();
        return await _dbContext.DeviceTokens.AnyAsync(token => token.Id == tokenId && (token.ExpiresAt == null || token.ExpiresAt > now),
            cancellationToken);
    }
}
