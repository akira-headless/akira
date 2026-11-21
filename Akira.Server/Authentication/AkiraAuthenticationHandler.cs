using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Core.Auth;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;

namespace Akira.Server.Authentication;

/// <summary>
/// Authenticates a device token sent as Authorization: Bearer or in the browser session cookie, or an API key sent as X-Api-Key.
/// </summary>
public sealed class AkiraAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AkiraDbContext _dbContext;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly TimeProvider _time;

    public const string ApiKeyHeader = "X-Api-Key";

    public const string FailureItem = "akira.authentication-failure";

    // Last-used times are for showing which devices are still in use
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    public AkiraAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AkiraDbContext dbContext,
        IOptions<AuthOptions> authOptions,
        TimeProvider time)
        : base(options, logger, encoder)
    {
        _dbContext = dbContext;
        _authOptions = authOptions;
        _time = time;
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var (secret, fromCookie) = ReadSecret();
        if (secret is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (fromCookie)
        {
            if (!BrowserSessions.IsSameOrigin(Request))
            {
                return Fail("The browser session only works from this server's own pages.");
            }

            if (BrowserSessions.ChangesState(Request.Method) && !Request.Headers.ContainsKey(BrowserSessions.RequestHeader))
            {
                return Fail($"A browser request that changes something needs the {BrowserSessions.RequestHeader} header.");
            }
        }

        var hash = Secrets.Hash(secret);
        var now = _time.GetUtcNow();

        if (secret.StartsWith(Secrets.DeviceTokenPrefix, StringComparison.Ordinal))
        {
            return await AuthenticateDeviceAsync(hash, now, fromCookie ? secret : null);
        }

        if (secret.StartsWith(Secrets.ApiKeyPrefix, StringComparison.Ordinal))
        {
            return await AuthenticateApiKeyAsync(hash, now);
        }

        return Fail("The credential is neither a device token nor an API key.");
    }

    private AuthenticateResult Fail(string reason)
    {
        Context.Items[FailureItem] = reason;
        return AuthenticateResult.Fail(reason);
    }

    private async Task<AuthenticateResult> AuthenticateDeviceAsync(string hash, DateTimeOffset now, string? cookie)
    {
        var token = await _dbContext.DeviceTokens.AsNoTracking()
            .Where(token => token.TokenHash == hash)
            .Select(token => new
            {
                token.Id,
                token.DeviceId,
                token.UserId,
                token.ExpiresAt,
                token.LastUsedAt,
                DeviceKind = token.Device.Kind,
                token.Device.Client,
                UserName = token.User == null ? null : token.User.Name,
                Role = token.User == null ? (UserRole?)null : token.User.Role
            })
            .SingleOrDefaultAsync(Context.RequestAborted);

        if (token is null)
        {
            return Fail("The device token is unknown or was revoked.");
        }

        if (token.DeviceKind == DeviceKind.Link)
        {
            return Fail("A link only works with the app it was made for.");
        }

        if (token.ExpiresAt <= now)
        {
            var reason = token.Client == BrowserSessions.Client
                ? "The browser session expired, sign in again."
                : "The profile session expired, pick the profile again.";
            return Fail(reason);
        }

        // A browser that is in use stays signed in: past half of its lifetime the session and its cookie start over
        var lifetime = _authOptions.Value.BrowserSessionLifetime;
        if (cookie is not null && token.Client == BrowserSessions.Client && token.ExpiresAt - now < lifetime / 2)
        {
            var renewed = now + lifetime;
            await _dbContext.DeviceTokens.Where(t => t.Id == token.Id)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.ExpiresAt, renewed), Context.RequestAborted);
            Response.Cookies.Append(BrowserSessions.CookieName, cookie, BrowserSessions.CookieOptions(Request, renewed));
        }

        if (token.LastUsedAt is null || now - token.LastUsedAt >= TouchInterval)
        {
            await _dbContext.DeviceTokens.Where(t => t.Id == token.Id)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.LastUsedAt, now), Context.RequestAborted);
            await _dbContext.Devices.Where(d => d.Id == token.DeviceId)
                .ExecuteUpdateAsync(d => d.SetProperty(x => x.LastSeenAt, now), Context.RequestAborted);
        }

        var credentialKind = token.DeviceKind == DeviceKind.Shared && token.UserId is not null
            ? AkiraClaims.ProfileCredential
            : AkiraClaims.DeviceCredential;
        var claims = new List<Claim>
        {
            new(AkiraClaims.TokenId, token.Id.ToString()),
            new(AkiraClaims.DeviceId, token.DeviceId.ToString()),
            new(AkiraClaims.DeviceKind, token.DeviceKind.ToString()),
            new(AkiraClaims.CredentialKind, credentialKind)
        };

        if (token.UserId is { } userId)
        {
            claims.Add(new Claim(AkiraClaims.UserId, userId.ToString()));
            claims.Add(new Claim(AkiraClaims.UserName, token.UserName!));
            claims.Add(new Claim(AkiraClaims.Role, token.Role!.Value.ToString()));
        }

        return Success(claims);
    }

    private async Task<AuthenticateResult> AuthenticateApiKeyAsync(string hash, DateTimeOffset now)
    {
        var key = await _dbContext.ApiKeys.AsNoTracking()
            .Where(key => key.KeyHash == hash)
            .Select(key => new { key.Id, key.UserId, key.LastUsedAt, key.User.Name, key.User.Role })
            .SingleOrDefaultAsync(Context.RequestAborted);

        if (key is null)
        {
            return Fail("The API key is unknown or was revoked.");
        }

        if (key.LastUsedAt is null || now - key.LastUsedAt >= TouchInterval)
        {
            await _dbContext.ApiKeys.Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(k => k.SetProperty(x => x.LastUsedAt, now), Context.RequestAborted);
        }

        return Success(
        [
            new Claim(AkiraClaims.TokenId, key.Id.ToString()),
            new Claim(AkiraClaims.CredentialKind, AkiraClaims.ApiKeyCredential),
            new Claim(AkiraClaims.UserId, key.UserId.ToString()),
            new Claim(AkiraClaims.UserName, key.Name),
            new Claim(AkiraClaims.Role, key.Role.ToString())
        ]);
    }

    private AuthenticateResult Success(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, AkiraClaims.Scheme, AkiraClaims.UserName, AkiraClaims.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), AkiraClaims.Scheme);
        return AuthenticateResult.Success(ticket);
    }

    // A header wins over the cookie, so a script or tool that names its credential is never mistaken for the browser
    private (string? Secret, bool FromCookie) ReadSecret()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return (authorization["Bearer ".Length..].Trim(), false);
        }

        var apiKey = Request.Headers[ApiKeyHeader].ToString();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return (apiKey.Trim(), false);
        }

        var cookie = Request.Cookies[BrowserSessions.CookieName];
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return (null, false);
        }

        return (cookie, true);
    }
}
