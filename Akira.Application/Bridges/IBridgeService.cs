using System.Security.Claims;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Bridges;

public interface IBridgeService
{
    /// <summary>
    /// Lists the switched on bridges with how an app connects to each, for the caller.
    /// </summary>
    IReadOnlyList<BridgeSummary> List(Uri serverBase);

    /// <summary>
    /// Makes a link for the caller and one link bridge: a device of kind link with a token only that bridge accepts.
    /// </summary>
    Task<ServiceResult<LinkCreated>> CreateLinkAsync(ClaimsPrincipal principal, CreateLinkRequest request, Uri serverBase,
        CancellationToken cancellationToken);
}
