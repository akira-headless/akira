using System.Net;
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

public class PairingService : IPairingService
{
    public const int PollIntervalSeconds = 5;

    // Pairing needs no credentials, so the number waiting at once is capped
    public const int MaxPending = 50;

    // A profile picked on a shared device lasts hours and may be left behind; what it makes would outlive it and its PIN
    public const string ProfileSessionRefusal = "A profile picked on a shared device cannot add devices, keys or links. Use a personal device.";

    private readonly AkiraDbContext _dbContext;
    private readonly IOptions<AuthOptions> _options;
    private readonly PairingStartLimiter _limiter;
    private readonly TimeProvider _time;
    private readonly ILogger<PairingService> _logger;

    public PairingService(
        AkiraDbContext dbContext,
        IOptions<AuthOptions> options,
        PairingStartLimiter limiter,
        TimeProvider time,
        ILogger<PairingService> logger)
    {
        _dbContext = dbContext;
        _options = options;
        _limiter = limiter;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PairingStarted>> StartAsync(StartPairingRequest request, IPAddress? from, CancellationToken cancellationToken)
    {
        var problem = AuthValidation.Text(request.DeviceName, "deviceName", AuthValidation.MaxNameLength)
                      ?? AuthValidation.Text(request.Client, "client", AuthValidation.MaxClientLength)
                      ?? AuthValidation.Text(request.ClientVersion, "clientVersion", AuthValidation.MaxClientLength, required: false)
                      ?? AuthValidation.Text(request.Platform, "platform", AuthValidation.MaxClientLength, required: false);
        if (problem is not null)
        {
            return ServiceResult<PairingStarted>.Fail(ServiceError.Invalid, problem);
        }

        if (!_limiter.TryStart(from))
        {
            return ServiceResult<PairingStarted>.Fail(ServiceError.TooMany,
                "This address started too many pairings. Finish or cancel them, or wait a few minutes.");
        }

        var now = _time.GetUtcNow();
        await RemoveExpiredAsync(now, cancellationToken);

        var pendingCodes = await _dbContext.PairingRequests
            .Where(pairing => pairing.Status == PairingStatus.Pending)
            .Select(pairing => pairing.Code)
            .ToListAsync(cancellationToken);
        if (pendingCodes.Count >= MaxPending)
        {
            return ServiceResult<PairingStarted>.Fail(ServiceError.TooMany,
                "Too many devices are waiting to be paired. Approve or deny them, or wait for them to expire.");
        }

        var code = PairingCodes.New();
        while (pendingCodes.Contains(code))
        {
            code = PairingCodes.New();
        }

        var pollSecret = Secrets.NewPollSecret();
        var pairing = new PairingRequest
        {
            Code = code,
            PollSecretHash = Secrets.Hash(pollSecret),
            DeviceName = request.DeviceName.Trim(),
            Client = request.Client.Trim(),
            ClientVersion = AuthValidation.Trimmed(request.ClientVersion),
            Platform = AuthValidation.Trimmed(request.Platform),
            ExpiresAt = now + _options.Value.PairingLifetime
        };
        _dbContext.PairingRequests.Add(pairing);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Device {DeviceName} ({Client}) is waiting to be paired with code {Code}",
            pairing.DeviceName, pairing.Client, pairing.Code);
        return ServiceResult<PairingStarted>.Ok(new PairingStarted(pairing.Id, code, pollSecret, pairing.ExpiresAt, PollIntervalSeconds));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PairingPollResult>> PollAsync(Guid pairingId, string pollSecret, CancellationToken cancellationToken)
    {
        var pairing = await _dbContext.PairingRequests.AsNoTracking()
            .Include(pairing => pairing.Device!).ThenInclude(device => device.User)
            .SingleOrDefaultAsync(pairing => pairing.Id == pairingId, cancellationToken);

        var pollSecretHash = Secrets.Hash(pollSecret ?? string.Empty);
        // A wrong secret looks the same as an unknown pairing, it says nothing about which ids exist
        if (pairing is null || !Secrets.FixedTimeEquals(pollSecretHash, pairing.PollSecretHash))
        {
            return ServiceResult<PairingPollResult>.Fail(ServiceError.NotFound, "Unknown pairing.");
        }

        var now = _time.GetUtcNow();
        if (pairing.ExpiresAt <= now)
        {
            return ServiceResult<PairingPollResult>.Fail(ServiceError.Gone, "The pairing code expired. Start pairing again.");
        }

        switch (pairing.Status)
        {
            case PairingStatus.Pending:
                var pending = new PairingPending(pairing.Status, pairing.ExpiresAt);
                return ServiceResult<PairingPollResult>.Ok(new PairingPollResult(pending, null));
            case PairingStatus.Denied:
                return ServiceResult<PairingPollResult>.Fail(ServiceError.Gone, "The pairing was denied.");
            case PairingStatus.Completed:
                return ServiceResult<PairingPollResult>.Fail(ServiceError.Gone, "The token was already handed out. Start pairing again.");
        }

        // Two polls racing each other must not both get a token
        var claimed = await _dbContext.PairingRequests
            .Where(p => p.Id == pairingId && p.Status == PairingStatus.Approved)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, PairingStatus.Completed), cancellationToken);
        if (claimed == 0)
        {
            return ServiceResult<PairingPollResult>.Fail(ServiceError.Gone, "The token was already handed out. Start pairing again.");
        }

        var token = Secrets.NewDeviceToken();
        var device = pairing.Device!;
        var deviceToken = new DeviceToken
        {
            DeviceId = device.Id,
            UserId = device.Kind == DeviceKind.Personal ? device.UserId : null,
            TokenHash = Secrets.Hash(token)
        };
        _dbContext.DeviceTokens.Add(deviceToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var completed = new PairingCompleted(token, device.ToSummary());
        return ServiceResult<PairingPollResult>.Ok(new PairingPollResult(null, completed));

    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingPairing>> ListPendingAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        return await _dbContext.PairingRequests.AsNoTracking()
            .Where(pairing => pairing.Status == PairingStatus.Pending && pairing.ExpiresAt > now)
            .OrderBy(pairing => pairing.CreatedAt)
            .Select(pairing => new PendingPairing(pairing.Code, pairing.DeviceName, pairing.Client, pairing.Platform,
                pairing.CreatedAt, pairing.ExpiresAt))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<DeviceSummary>> ApproveAsync(
        ClaimsPrincipal approver,
        ApprovePairingRequest request,
        CancellationToken cancellationToken)
    {
        if (approver.IsProfileSession())
        {
            return ServiceResult<DeviceSummary>.Fail(ServiceError.Forbidden, ProfileSessionRefusal);
        }

        var pairing = await FindPendingAsync(request.Code, cancellationToken);
        if (pairing is null)
        {
            return ServiceResult<DeviceSummary>.Fail(ServiceError.NotFound, "No device is waiting with that code.");
        }

        if (request.Kind is not (DeviceKind.Personal or DeviceKind.Shared))
        {
            return ServiceResult<DeviceSummary>.Fail(ServiceError.Invalid, "kind is personal or shared.");
        }

        User? owner = null;
        if (request.Kind == DeviceKind.Shared)
        {
            if (!approver.IsAdmin())
            {
                return ServiceResult<DeviceSummary>.Fail(ServiceError.Forbidden, "Only an admin can approve a shared device.");
            }

            if (request.UserId is not null)
            {
                return ServiceResult<DeviceSummary>.Fail(ServiceError.Invalid, "A shared device has no userId, its users pick a profile.");
            }
        }
        else
        {
            var ownerId = request.UserId ?? approver.GetUserId()!.Value;
            if (ownerId != approver.GetUserId() && !approver.IsAdmin())
            {
                return ServiceResult<DeviceSummary>.Fail(ServiceError.Forbidden, "Only an admin can approve a device for someone else.");
            }

            owner = await _dbContext.Users.SingleOrDefaultAsync(user => user.Id == ownerId, cancellationToken);
            if (owner is null)
            {
                return ServiceResult<DeviceSummary>.Fail(ServiceError.NotFound, $"User {ownerId} does not exist.");
            }
        }

        // Two approvals racing each other must not both make a device: the second would be left without a token.
        // In a transaction, so an approval that cannot be saved is pending again.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var claimed = await _dbContext.PairingRequests
            .Where(p => p.Id == pairing.Id && p.Status == PairingStatus.Pending)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, PairingStatus.Approved), cancellationToken);
        if (claimed == 0)
        {
            return ServiceResult<DeviceSummary>.Fail(ServiceError.NotFound, "No device is waiting with that code.");
        }

        var device = new Device
        {
            Name = pairing.DeviceName,
            Client = pairing.Client,
            ClientVersion = pairing.ClientVersion,
            Platform = pairing.Platform,
            Kind = request.Kind,
            UserId = owner?.Id,
            User = owner
        };
        _dbContext.Devices.Add(device);

        pairing.Status = PairingStatus.Approved;
        pairing.DeviceId = device.Id;

        // A fresh window to collect the token, however long the approval took
        pairing.ExpiresAt = _time.GetUtcNow() + _options.Value.PairingLifetime;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("{Approver} approved {DeviceName} as a {Kind} device{Owner}",
            approver.Identity?.Name, device.Name, device.Kind, owner is null ? "" : $" of {owner.Name}");
        return ServiceResult<DeviceSummary>.Ok(device.ToSummary());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DenyAsync(string code, CancellationToken cancellationToken)
    {
        var pairing = await FindPendingAsync(code, cancellationToken);
        if (pairing is null)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, "No device is waiting with that code.");
        }

        pairing.Status = PairingStatus.Denied;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }

    private async Task<PairingRequest?> FindPendingAsync(string? code, CancellationToken cancellationToken)
    {
        var normalized = PairingCodes.Normalize(code);
        if (normalized is null)
        {
            return null;
        }

        var now = _time.GetUtcNow();
        return await _dbContext.PairingRequests
            .Where(pairing => pairing.Code == normalized && pairing.Status == PairingStatus.Pending && pairing.ExpiresAt > now)
            .OrderByDescending(pairing => pairing.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A device that was approved but never collected its token has no way to sign in
        var abandoned = _dbContext.PairingRequests
            .Where(pairing => pairing.Status == PairingStatus.Approved && pairing.ExpiresAt <= now && pairing.DeviceId != null)
            .Select(pairing => pairing.DeviceId!.Value);
        await _dbContext.Devices.Where(device => abandoned.Contains(device.Id)).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.PairingRequests.Where(pairing => pairing.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
    }
}
