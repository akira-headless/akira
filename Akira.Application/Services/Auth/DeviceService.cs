using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Akira.Server.Authentication;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;

namespace Akira.Server.Services.Auth;

public class DeviceService : IDeviceService
{
    private readonly AkiraDbContext _dbContext;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(
        AkiraDbContext dbContext,
        ILogger<DeviceService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceSummary>> ListAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var isAdmin = principal.IsAdmin();
        var devices = await _dbContext.Devices.AsNoTracking()
            .Include(device => device.User)
            .Where(device => isAdmin || device.UserId == userId)
            .OrderBy(device => device.Name)
            .ToListAsync(cancellationToken);

        return devices.Select(device => device.ToSummary()).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> RevokeAsync(ClaimsPrincipal principal, Guid id, CancellationToken cancellationToken)
    {
        var device = await _dbContext.Devices.SingleOrDefaultAsync(device => device.Id == id, cancellationToken);
        if (device is null)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, $"Device {id} does not exist.");
        }

        if (!principal.IsAdmin() && device.UserId != principal.GetUserId())
        {
            return ServiceResult<bool>.Fail(ServiceError.Forbidden, "Only an admin can revoke someone else's or a shared device.");
        }

        _dbContext.Devices.Remove(device);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("{User} revoked device {DeviceName}", principal.Identity?.Name, device.Name);
        return ServiceResult<bool>.Ok(true);
    }
}
