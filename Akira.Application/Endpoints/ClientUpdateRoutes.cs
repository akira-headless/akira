namespace Akira.Server.Endpoints;

/// <summary>
/// Defines the client update routes, so the url handed to apps and the mapped route come from one place.
/// </summary>
public static class ClientUpdateRoutes
{
    public const string ClientUpdates = "/client-updates";
    public const string Platform = "{platform}";
    public const string File = "{platform}/file";
    public const string Check = "{platform}/check";

    public static string FileUrl(string platform) => $"{ItemRoutes.ApiPrefix}{ClientUpdates}/{platform}/file";
}
