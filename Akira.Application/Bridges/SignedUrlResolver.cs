using Microsoft.EntityFrameworkCore;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;

namespace Akira.Server.Bridges;

public sealed class SignedUrlResolver : ISignedUrlResolver
{
    private readonly SignedUrls _urls;
    private readonly AkiraDbContext _dbContext;
    private readonly TimeProvider _time;

    public SignedUrlResolver(
        SignedUrls urls,
        AkiraDbContext dbContext,
        TimeProvider time)
    {
        _urls = urls;
        _dbContext = dbContext;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<SignedTarget?> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        if (_urls.Read(token) is not { } target)
        {
            return null;
        }

        // The device still exists, and so does the credential the url was made for: a profile session that ended or
        // gave way to another profile takes its urls with it
        var now = _time.GetUtcNow();
        var tokenId = target.TokenId;
        var device = await _dbContext.Devices.AsNoTracking()
            .Where(d => d.Id == target.DeviceId)
            .Select(d => new
            {
                d.Kind,
                HasTokens = d.Tokens.Any(),
                Live = tokenId == null || d.Tokens.Any(t => t.Id == tokenId && (t.ExpiresAt == null || t.ExpiresAt > now))
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (device is not { Live: true })
        {
            return null;
        }

        // A link without a token is the fixed user of a bridge without sign in
        var localOnly = device.Kind == DeviceKind.Link && !device.HasTokens;
        return target with { LocalOnly = localOnly };
    }
}
