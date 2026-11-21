using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Akira.Server.Authentication;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Actions;

namespace Akira.Server.Services.Actions;

public class RemovalSettingsService : IRemovalSettingsService
{
    // What removing does when nobody chose otherwise: it does not come back
    private const RemovalBehavior Default = RemovalBehavior.DeleteAndUnmonitor;

    private readonly AkiraDbContext _dbContext;

    public RemovalSettingsService(AkiraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<ServerRemovalSettings> GetServerAsync(CancellationToken cancellationToken)
    {
        var row = await FindAsync(RemovalSetting.ServerKey, cancellationToken);
        return ToServer(row);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ServerRemovalSettings>> SetServerAsync(ServerRemovalSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Defaults is null)
        {
            return ServiceResult<ServerRemovalSettings>.Fail(ServiceError.Invalid, "defaults is required, with a behavior for movie, episode, season and series.");
        }

        var row = await GetOrAddAsync(RemovalSetting.ServerKey, null, cancellationToken);
        row.Movie = settings.Defaults.Movie;
        row.Episode = settings.Defaults.Episode;
        row.Season = settings.Defaults.Season;
        row.Series = settings.Defaults.Series;
        row.UsersMayRemove = settings.UsersMayRemove;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<ServerRemovalSettings>.Ok(ToServer(row));
    }

    /// <inheritdoc />
    public async Task<UserRemovalSettings> GetForUserAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId()!.Value;
        var server = await FindAsync(RemovalSetting.ServerKey, cancellationToken);
        var own = await FindAsync(UserKey(userId), cancellationToken);
        return Describe(user, server, own);
    }

    /// <inheritdoc />
    public async Task<UserRemovalSettings> SetForUserAsync(ClaimsPrincipal user, RemovalBehaviors behaviors, CancellationToken cancellationToken)
    {
        var userId = user.GetUserId()!.Value;
        var row = await GetOrAddAsync(UserKey(userId), userId, cancellationToken);
        row.Movie = behaviors.Movie;
        row.Episode = behaviors.Episode;
        row.Season = behaviors.Season;
        row.Series = behaviors.Series;
        await _dbContext.SaveChangesAsync(cancellationToken);
        var server = await FindAsync(RemovalSetting.ServerKey, cancellationToken);
        return Describe(user, server, row);
    }

    /// <inheritdoc />
    public async Task<bool> MayRemoveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (user.IsAdmin())
        {
            return true;
        }

        var server = await FindAsync(RemovalSetting.ServerKey, cancellationToken);
        return server?.UsersMayRemove == true;
    }

    private UserRemovalSettings Describe(ClaimsPrincipal user, RemovalSetting? server, RemovalSetting? own)
    {
        var defaults = ToServer(server).Defaults;
        var effective = new EffectiveRemoval(
            own?.Movie ?? defaults.Movie,
            own?.Episode ?? defaults.Episode,
            own?.Season ?? defaults.Season,
            own?.Series ?? defaults.Series);
        var chosen = new RemovalBehaviors(own?.Movie, own?.Episode, own?.Season, own?.Series);
        var mayRemove = user.IsAdmin() || server?.UsersMayRemove == true;
        return new UserRemovalSettings(chosen, effective, mayRemove);
    }

    private static ServerRemovalSettings ToServer(RemovalSetting? row)
    {
        var defaults = new EffectiveRemoval(row?.Movie ?? Default, row?.Episode ?? Default, row?.Season ?? Default, row?.Series ?? Default);
        return new ServerRemovalSettings(defaults, row?.UsersMayRemove == true);
    }

    private Task<RemovalSetting?> FindAsync(string key, CancellationToken cancellationToken) =>
        _dbContext.RemovalSettings.SingleOrDefaultAsync(setting => setting.ScopeKey == key, cancellationToken);

    private async Task<RemovalSetting> GetOrAddAsync(string key, Guid? userId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(key, cancellationToken);
        if (row is null)
        {
            row = new RemovalSetting { ScopeKey = key, UserId = userId };
            _dbContext.RemovalSettings.Add(row);
        }

        return row;
    }

    private static string UserKey(Guid userId) => userId.ToString("N");
}
