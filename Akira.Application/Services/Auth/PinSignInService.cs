using Microsoft.EntityFrameworkCore;
using Akira.Server.Authentication;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;

namespace Akira.Server.Services.Auth;

public class PinSignInService : IPinSignInService
{
    // Wrong PINs here count per user across every address, apart from the profile lockouts of shared devices, so guessing
    // from several devices at once gets no more tries
    private static readonly Guid SignInAttempts = Guid.Empty;

    private readonly AkiraDbContext _dbContext;
    private readonly PinAttemptLimiter _limiter;
    private readonly ILogger<PinSignInService> _logger;

    public PinSignInService(
        AkiraDbContext dbContext,
        PinAttemptLimiter limiter,
        ILogger<PinSignInService> logger)
    {
        _dbContext = dbContext;
        _limiter = limiter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PinUser>> ListAsync(CancellationToken cancellationToken) =>
        await _dbContext.Users.AsNoTracking()
            .Where(user => user.PinHash != null)
            .OrderBy(user => user.Name)
            .Select(user => new PinUser(user.Id, user.Name))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ServiceResult<PairingCompleted>> SignInAsync(PinSignInRequest request, CancellationToken cancellationToken)
    {
        var problem = AuthValidation.Text(request.DeviceName, "deviceName", AuthValidation.MaxNameLength)
                      ?? AuthValidation.Text(request.Client, "client", AuthValidation.MaxClientLength)
                      ?? AuthValidation.Text(request.ClientVersion, "clientVersion", AuthValidation.MaxClientLength, required: false)
                      ?? AuthValidation.Text(request.Platform, "platform", AuthValidation.MaxClientLength, required: false);
        if (problem is not null)
        {
            return ServiceResult<PairingCompleted>.Fail(ServiceError.Invalid, problem);
        }

        // Someone without a PIN cannot sign in this way, and the answer does not say whether they exist
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Id == request.UserId && user.PinHash != null, cancellationToken);
        if (user is null)
        {
            return ServiceResult<PairingCompleted>.Fail(ServiceError.NotFound, "Nobody with a PIN has that id.");
        }

        if (!_limiter.TryBegin(SignInAttempts, user.Id, out var remaining, out var locked))
        {
            return ServiceResult<PairingCompleted>.Fail(ServiceError.LockedOut,
                $"Too many wrong PINs. Try again in {Math.Ceiling(locked.TotalMinutes)} minutes.", locked);
        }

        if (!Pins.Verify(request.Pin, user.PinHash!))
        {
            _logger.LogWarning("A wrong PIN for {User} from a device signing in as {DeviceName}", user.Name, request.DeviceName.Trim());
            if (remaining == 0)
            {
                return ServiceResult<PairingCompleted>.Fail(ServiceError.LockedOut, "Too many wrong PINs.", locked);
            }

            return ServiceResult<PairingCompleted>.Fail(ServiceError.WrongPin, $"Wrong PIN, {remaining} attempts left.");
        }

        _limiter.Reset(SignInAttempts, user.Id);
        var device = new Device
        {
            Name = request.DeviceName.Trim(),
            Client = request.Client.Trim(),
            ClientVersion = AuthValidation.Trimmed(request.ClientVersion),
            Platform = AuthValidation.Trimmed(request.Platform),
            Kind = DeviceKind.Personal,
            UserId = user.Id,
            User = user
        };
        _dbContext.Devices.Add(device);

        var token = Secrets.NewDeviceToken();
        var deviceToken = new DeviceToken { DeviceId = device.Id, UserId = user.Id, TokenHash = Secrets.Hash(token) };
        _dbContext.DeviceTokens.Add(deviceToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("{User} signed in {DeviceName} with their PIN", user.Name, device.Name);
        return ServiceResult<PairingCompleted>.Ok(new PairingCompleted(token, device.ToSummary()));
    }
}
