using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Akira.Server.Authentication;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;

namespace Akira.Server.Services.Auth;

public class UserService : IUserService
{
    // Role changes and deletions go one at a time: two admins demoting each other at once would each still see the other as admin
    private static readonly SemaphoreSlim AdminChanges = new(1, 1);

    private readonly AkiraDbContext _dbContext;
    private readonly PinAttemptLimiter _limiter;

    public UserService(
        AkiraDbContext dbContext,
        PinAttemptLimiter limiter)
    {
        _dbContext = dbContext;
        _limiter = limiter;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CurrentUser>> GetCurrentAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == principal.GetUserId(), cancellationToken);
        if (user is null)
        {
            return ServiceResult<CurrentUser>.Fail(ServiceError.NotFound, "The user no longer exists.");
        }

        var deviceId = principal.GetDeviceId();
        Device? device = null;
        if (deviceId is not null)
        {
            device = await _dbContext.Devices.AsNoTracking()
                .Include(device => device.User)
                .SingleOrDefaultAsync(device => device.Id == deviceId, cancellationToken);
        }

        var credential = principal.FindFirstValue(AkiraClaims.CredentialKind)!;
        return ServiceResult<CurrentUser>.Ok(new CurrentUser(user.ToSummary(), device?.ToSummary(), credential));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users.AsNoTracking().OrderBy(user => user.Name).ToListAsync(cancellationToken);
        return users.Select(user => user.ToSummary()).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserSummary>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (!UserNames.IsValid(request.Name))
        {
            return ServiceResult<UserSummary>.Fail(ServiceError.Invalid, $"A name is 1 to {UserNames.MaxLength} characters.");
        }

        if (!Enum.IsDefined(request.Role))
        {
            return ServiceResult<UserSummary>.Fail(ServiceError.Invalid, "role is admin or user.");
        }

        if (request.Pin is not null && !Pins.IsValid(request.Pin))
        {
            return ServiceResult<UserSummary>.Fail(ServiceError.Invalid, $"A PIN is {Pins.MinLength} to {Pins.MaxLength} digits.");
        }

        var normalized = UserNames.Normalize(request.Name);
        if (await _dbContext.Users.AnyAsync(user => user.NormalizedName == normalized, cancellationToken))
        {
            return ServiceResult<UserSummary>.Fail(ServiceError.Conflict, $"A user called {request.Name.Trim()} already exists.");
        }

        var user = new User
        {
            Name = request.Name.Trim(),
            NormalizedName = normalized,
            Role = request.Role,
            PinHash = request.Pin is null ? null : Pins.Hash(request.Pin)
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<UserSummary>.Ok(user.ToSummary());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserSummary>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await AdminChanges.WaitAsync(cancellationToken);
        try
        {
            var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
            if (user is null)
            {
                return ServiceResult<UserSummary>.Fail(ServiceError.NotFound, $"User {id} does not exist.");
            }

            if (request.Name is not null)
            {
                if (!UserNames.IsValid(request.Name))
                {
                    return ServiceResult<UserSummary>.Fail(ServiceError.Invalid, $"A name is 1 to {UserNames.MaxLength} characters.");
                }

                var normalized = UserNames.Normalize(request.Name);
                if (await _dbContext.Users.AnyAsync(other => other.Id != id && other.NormalizedName == normalized, cancellationToken))
                {
                    return ServiceResult<UserSummary>.Fail(ServiceError.Conflict, $"A user called {request.Name.Trim()} already exists.");
                }

                user.Name = request.Name.Trim();
                user.NormalizedName = normalized;
            }

            if (request.Role is { } role && role != user.Role)
            {
                if (!Enum.IsDefined(role))
                {
                    return ServiceResult<UserSummary>.Fail(ServiceError.Invalid, "role is admin or user.");
                }

                if (user.Role == UserRole.Admin && await IsLastAdminAsync(user.Id, cancellationToken))
                {
                    return ServiceResult<UserSummary>.Fail(ServiceError.Conflict, "This is the last admin. Make someone else admin first.");
                }

                user.Role = role;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<UserSummary>.Ok(user.ToSummary());
        }
        finally
        {
            AdminChanges.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await AdminChanges.WaitAsync(cancellationToken);
        try
        {
            var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
            if (user is null)
            {
                return ServiceResult<bool>.Fail(ServiceError.NotFound, $"User {id} does not exist.");
            }

            if (user.Role == UserRole.Admin && await IsLastAdminAsync(user.Id, cancellationToken))
            {
                return ServiceResult<bool>.Fail(ServiceError.Conflict, "This is the last admin. Make someone else admin first.");
            }

            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ServiceResult<bool>.Ok(true);
        }
        finally
        {
            AdminChanges.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserSummary>> SetPinAsync(
        ClaimsPrincipal principal,
        Guid userId,
        SetPinRequest request,
        CancellationToken cancellationToken)
    {
        var isSelf = principal.GetUserId() == userId;
        if (!isSelf && !principal.IsAdmin())
        {
            return ServiceResult<UserSummary>.Fail(ServiceError.Forbidden, "Only an admin can change someone else's PIN.");
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<UserSummary>.Fail(ServiceError.NotFound, $"User {userId} does not exist.");
        }

        if (request.NewPin is not null && !Pins.IsValid(request.NewPin))
        {
            return ServiceResult<UserSummary>.Fail(ServiceError.Invalid, $"A PIN is {Pins.MinLength} to {Pins.MaxLength} digits.");
        }

        // A first PIN signs in a personal device later, which would outlive the profile session that set it
        if (isSelf && user.PinHash is null && request.NewPin is not null && principal.IsProfileSession())
        {
            return ServiceResult<UserSummary>.Fail(ServiceError.Forbidden,
                "A profile picked on a shared device cannot set a first PIN. Use a personal device.");
        }

        // Changing your own PIN needs the current one, so a device left signed in cannot be used to take it over
        if (isSelf && user.PinHash is not null)
        {
            var attemptKey = principal.GetTokenId() ?? Guid.Empty;
            if (!_limiter.TryBegin(attemptKey, userId, out var remaining, out var locked))
            {
                return ServiceResult<UserSummary>.Fail(ServiceError.LockedOut, "Too many wrong PINs.", locked);
            }

            if (!Pins.Verify(request.CurrentPin, user.PinHash))
            {
                return ServiceResult<UserSummary>.Fail(remaining == 0 ? ServiceError.LockedOut : ServiceError.WrongPin,
                    $"The current PIN is wrong, {remaining} attempts left.", remaining == 0 ? locked : null);
            }

            _limiter.Reset(attemptKey, userId);
        }

        user.PinHash = request.NewPin is null ? null : Pins.Hash(request.NewPin);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<UserSummary>.Ok(user.ToSummary());
    }

    private async Task<bool> IsLastAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        !await _dbContext.Users.AnyAsync(other => other.Id != userId && other.Role == UserRole.Admin, cancellationToken);
}
