using System.Security.Claims;
using Akira.Core.Enum;

namespace Akira.Server.Authentication;

public static class AkiraClaims
{
    public const string Scheme = "Akira";

    public const string UserId = "sub";
    public const string UserName = "name";
    public const string Role = ClaimTypes.Role;
    public const string DeviceId = "device_id";
    public const string DeviceKind = "device_kind";
    public const string TokenId = "token_id";
    public const string CredentialKind = "credential";

    public const string DeviceCredential = "device";
    public const string ProfileCredential = "profile";
    public const string ApiKeyCredential = "apiKey";

    public static Guid? GetUserId(this ClaimsPrincipal principal) => GetGuid(principal, UserId);

    public static Guid? GetDeviceId(this ClaimsPrincipal principal) => GetGuid(principal, DeviceId);

    public static Guid? GetTokenId(this ClaimsPrincipal principal) => GetGuid(principal, TokenId);

    public static bool IsAdmin(this ClaimsPrincipal principal) => principal.IsInRole(nameof(UserRole.Admin));

    public static bool IsProfileSession(this ClaimsPrincipal principal) => principal.FindFirstValue(CredentialKind) == ProfileCredential;

    public static bool IsSharedDevice(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(DeviceKind) == nameof(Core.Enum.DeviceKind.Shared);

    private static Guid? GetGuid(ClaimsPrincipal principal, string type) =>
        Guid.TryParse(principal.FindFirstValue(type), out var value) ? value : null;
}
