using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Akira.Server.Authentication;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;

namespace Akira.Server.Services.Auth;

public class ApiKeyService : IApiKeyService
{
    private readonly AkiraDbContext _dbContext;

    public ApiKeyService(AkiraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKeySummary>> ListAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = principal.GetUserId();
        var isAdmin = principal.IsAdmin();
        var keys = await _dbContext.ApiKeys.AsNoTracking()
            .Include(key => key.User)
            .Where(key => isAdmin || key.UserId == userId)
            .OrderBy(key => key.CreatedAt)
            .ToListAsync(cancellationToken);

        return keys.Select(key => key.ToSummary()).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CreatedApiKey>> CreateAsync(
        ClaimsPrincipal principal,
        CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        if (principal.IsProfileSession())
        {
            return ServiceResult<CreatedApiKey>.Fail(ServiceError.Forbidden, PairingService.ProfileSessionRefusal);
        }

        if (AuthValidation.Text(request.Name, "name", AuthValidation.MaxNameLength) is { } problem)
        {
            return ServiceResult<CreatedApiKey>.Fail(ServiceError.Invalid, problem);
        }

        var user = await _dbContext.Users.SingleAsync(user => user.Id == principal.GetUserId(), cancellationToken);
        var secret = Secrets.NewApiKey();
        var key = new ApiKey
        {
            UserId = user.Id,
            User = user,
            Name = request.Name.Trim(),
            KeyHash = Secrets.Hash(secret),
            Prefix = secret[..Secrets.DisplayPrefixLength]
        };
        _dbContext.ApiKeys.Add(key);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<CreatedApiKey>.Ok(new CreatedApiKey(key.ToSummary(), secret));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> RevokeAsync(ClaimsPrincipal principal, Guid id, CancellationToken cancellationToken)
    {
        var key = await _dbContext.ApiKeys.SingleOrDefaultAsync(key => key.Id == id, cancellationToken);
        if (key is null)
        {
            return ServiceResult<bool>.Fail(ServiceError.NotFound, $"API key {id} does not exist.");
        }

        if (!principal.IsAdmin() && key.UserId != principal.GetUserId())
        {
            return ServiceResult<bool>.Fail(ServiceError.Forbidden, "Only an admin can revoke someone else's API key.");
        }

        _dbContext.ApiKeys.Remove(key);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Ok(true);
    }
}
