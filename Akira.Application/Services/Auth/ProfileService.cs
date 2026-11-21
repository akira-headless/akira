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

public class ProfileService : IProfileService
{
    private readonly AkiraDbContext _dbContext;
    private readonly PinAttemptLimiter _limiter;
    private readonly IOptions<AuthOptions> _options;
    private readonly TimeProvider _time;

    public ProfileService(
        AkiraDbContext dbContext,
        PinAttemptLimiter limiter,
        IOptions<AuthOptions> options,
        TimeProvider time)
    {
        _dbContext = dbContext;
        _limiter = limiter;
        _options = options;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProfileSummary>> ListAsync(ClaimsPrincipal device, CancellationToken cancellationToken)
    {
        var deviceId = device.GetDeviceId()!.Value;
        var owner = await _dbContext.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.Kind, d.UserId })
            .SingleAsync(cancellationToken);

        // An admin without a PIN would be one tap away for whoever holds the remote, so a shared device leaves them out
        return await _dbContext.Users.AsNoTracking()
            .Where(user => owner.Kind == DeviceKind.Shared || user.Id == owner.UserId)
            .Where(user => owner.Kind != DeviceKind.Shared || user.Role != UserRole.Admin || user.PinHash != null)
            .OrderBy(user => user.Name)
            .Select(user => new ProfileSummary(user.Id, user.Name, user.PinHash != null))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProfileSession>> StartSessionAsync(
        ClaimsPrincipal device,
        Guid userId,
        string? pin,
        CancellationToken cancellationToken)
    {
        var deviceId = device.GetDeviceId()!.Value;
        if (!device.IsSharedDevice())
        {
            return ServiceResult<ProfileSession>.Fail(ServiceError.Forbidden,
                "This is a personal device, it is always signed in as its owner.");
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<ProfileSession>.Fail(ServiceError.NotFound, $"User {userId} does not exist.");
        }

        if (user.Role == UserRole.Admin && user.PinHash is null)
        {
            return ServiceResult<ProfileSession>.Fail(ServiceError.Forbidden,
                "An admin needs a PIN to use a shared device. Set one under Profile on a personal device.");
        }

        if (user.PinHash is not null)
        {
            if (!_limiter.TryBegin(deviceId, userId, out var remaining, out var locked))
            {
                return ServiceResult<ProfileSession>.Fail(ServiceError.LockedOut,
                    $"Too many wrong PINs. Try again in {Math.Ceiling(locked.TotalMinutes)} minutes.", locked);
            }

            if (!Pins.Verify(pin, user.PinHash))
            {
                return remaining == 0
                    ? ServiceResult<ProfileSession>.Fail(ServiceError.LockedOut, "Too many wrong PINs.", locked)
                    : ServiceResult<ProfileSession>.Fail(ServiceError.WrongPin, $"Wrong PIN, {remaining} attempts left.");
            }

            _limiter.Reset(deviceId, userId);
        }

        var now = _time.GetUtcNow();

        // One profile at a time: picking one ends every session of this device, whichever credential asked. A user id
        // marks a profile session, the device's own token has none.
        await _dbContext.DeviceTokens
            .Where(token => token.DeviceId == deviceId && (token.ExpiresAt <= now || token.UserId != null))
            .ExecuteDeleteAsync(cancellationToken);

        var secret = Secrets.NewDeviceToken();
        var token = new DeviceToken
        {
            DeviceId = deviceId,
            UserId = user.Id,
            TokenHash = Secrets.Hash(secret),
            ExpiresAt = now + _options.Value.ProfileSessionLifetime
        };
        _dbContext.DeviceTokens.Add(token);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ProfileSession>.Ok(new ProfileSession(secret, token.ExpiresAt.Value, user.ToSummary()));
    }
}
