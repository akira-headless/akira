using System.Security.Claims;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeySummary>> ListAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an API key for the caller. The key is only ever returned here.
    /// </summary>
    Task<ServiceResult<CreatedApiKey>> CreateAsync(ClaimsPrincipal principal, CreateApiKeyRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> RevokeAsync(ClaimsPrincipal principal, Guid id, CancellationToken cancellationToken);
}
