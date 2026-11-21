using System.Security.Claims;
using Akira.Contracts.Accounts;
using Akira.Server.Authentication;
using Akira.Core.Enum;

namespace Akira.Server.Bridges;

/// <summary>
/// Returns the claims a bridge's caller would have as a personal device, so the facades call the same services as the API and
/// every rule about whose session, whose watch state and who may do what applies unchanged.
/// </summary>
public static class BridgePrincipal
{
    public static ClaimsPrincipal For(Caller caller)
    {
        var role = caller.IsAdmin ? nameof(UserRole.Admin) : nameof(UserRole.User);
        Claim[] claims =
        [
            new Claim(AkiraClaims.UserId, caller.UserId.ToString()),
            new Claim(AkiraClaims.UserName, caller.UserName),
            new Claim(AkiraClaims.Role, role),
            new Claim(AkiraClaims.DeviceId, caller.DeviceId.ToString()),
            new Claim(AkiraClaims.DeviceKind, nameof(DeviceKind.Personal)),
            new Claim(AkiraClaims.CredentialKind, AkiraClaims.DeviceCredential)
        ];

        var identity = new ClaimsIdentity(claims, AkiraClaims.Scheme, AkiraClaims.UserName, AkiraClaims.Role);
        return new ClaimsPrincipal(identity);
    }
}
