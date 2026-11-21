using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Akira.Server.Authentication;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;

namespace Akira.Server.Services.Auth;

public class BrowserSessionService : IBrowserSessionService
{
    private static readonly TimeSpan Leftover = TimeSpan.FromDays(1);

    private readonly AkiraDbContext _dbContext;
    private readonly IUserService _users;
    private readonly IOptions<AuthOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BrowserSessionService> _logger;

    public BrowserSessionService(
        AkiraDbContext dbContext,
        IUserService users,
        IOptions<AuthOptions> options,
        TimeProvider time,
        ILogger<BrowserSessionService> logger)
    {
        _dbContext = dbContext;
        _users = users;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<BrowserSession>> StartAsync(ClaimsPrincipal principal, BrowserSessionRequest request, CancellationToken cancellationToken)
    {
        var credential = principal.FindFirstValue(AkiraClaims.CredentialKind);
        var userId = principal.GetUserId();
        if (userId is null || credential == AkiraClaims.ProfileCredential || principal.IsSharedDevice())
        {
            return ServiceResult<BrowserSession>.Fail(ServiceError.Invalid,
                "A browser signs in as a personal device: use an API key, or pair it as a personal device.");
        }

        var error = AuthValidation.Text(request.Name, "name", AuthValidation.MaxNameLength, required: false)
                    ?? AuthValidation.Text(request.Platform, "platform", AuthValidation.MaxClientLength, required: false);
        if (error is not null)
        {
            return ServiceResult<BrowserSession>.Fail(ServiceError.Invalid, error);
        }

        // Each sign in with an API key makes a device, so what sessions that ran out left behind goes here. A browser
        // being paired has a device and no token yet, which is why a device gets a day first.
        var now = _time.GetUtcNow();
        await _dbContext.DeviceTokens
            .Where(token => token.ExpiresAt <= now && token.Device.Client == BrowserSessions.Client)
            .ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Devices
            .Where(device => device.Client == BrowserSessions.Client && !device.Tokens.Any() && device.CreatedAt < now - Leftover)
            .ExecuteDeleteAsync(cancellationToken);

        Guid deviceId;
        if (credential == AkiraClaims.ApiKeyCredential)
        {
            var device = new Device
            {
                Name = AuthValidation.Trimmed(request.Name) ?? "Browser",
                Client = BrowserSessions.Client,
                Platform = AuthValidation.Trimmed(request.Platform),
                Kind = DeviceKind.Personal,
                UserId = userId,
                LastSeenAt = _time.GetUtcNow()
            };
            _dbContext.Devices.Add(device);
            deviceId = device.Id;
        }
        else
        {
            // Only a paired browser: the token of another app pasted here would sign that device out and hand its device
            // to a cookie
            deviceId = principal.GetDeviceId()!.Value;
            var client = await _dbContext.Devices
                .Where(d => d.Id == deviceId)
                .Select(d => d.Client)
                .SingleOrDefaultAsync(cancellationToken);
            if (client != BrowserSessions.Client)
            {
                return ServiceResult<BrowserSession>.Fail(ServiceError.Forbidden,
                    "This token belongs to another app. Sign in with an API key, or pair this browser.");
            }

            // A paired browser keeps its device; the token it got from pairing was in its scripts' hands, so it is replaced
            await _dbContext.DeviceTokens.Where(token => token.Id == principal.GetTokenId()).ExecuteDeleteAsync(cancellationToken);
        }

        var secret = Secrets.NewDeviceToken();
        var expiresAt = now + _options.Value.BrowserSessionLifetime;
        var deviceToken = new DeviceToken { DeviceId = deviceId, UserId = userId, TokenHash = Secrets.Hash(secret), ExpiresAt = expiresAt };
        _dbContext.DeviceTokens.Add(deviceToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Browser device {DeviceId} signed in for user {UserId}", deviceId, userId);

        var signedIn = WithDevice(principal, deviceId);
        var current = await _users.GetCurrentAsync(signedIn, cancellationToken);
        if (!current.Succeeded)
        {
            return ServiceResult<BrowserSession>.Fail(current.Error!.Value, current.Detail!);
        }

        var currentUser = current.Value! with { Credential = AkiraClaims.DeviceCredential };
        return ServiceResult<BrowserSession>.Ok(new BrowserSession(secret, expiresAt, currentUser));
    }

    /// <inheritdoc />
    public async Task EndAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.GetDeviceId() is not { } deviceId)
        {
            return;
        }

        // Only a browser's own device goes; signing out with another app's token by mistake does not unpair that app
        var removed = await _dbContext.Devices
            .Where(device => device.Id == deviceId && device.Client == BrowserSessions.Client)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
        {
            _logger.LogInformation("Browser device {DeviceId} signed out", deviceId);
        }
    }

    private static ClaimsPrincipal WithDevice(ClaimsPrincipal principal, Guid deviceId)
    {
        var deviceClaim = new Claim(AkiraClaims.DeviceId, deviceId.ToString());
        var claims = principal.Claims.Where(claim => claim.Type != AkiraClaims.DeviceId).Append(deviceClaim);
        var identity = new ClaimsIdentity(claims, AkiraClaims.Scheme, AkiraClaims.UserName, AkiraClaims.Role);
        return new ClaimsPrincipal(identity);
    }
}
