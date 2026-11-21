namespace Akira.Server.Authentication;

/// <summary>
/// Defines how a browser holds its device token: in a cookie its scripts cannot read.
/// </summary>
public static class BrowserSessions
{
    public const string CookieName = "akira_session";

    // A header another site cannot send without CORS, which this server does not allow, so a request that
    // changes something always comes from the server's own pages
    public const string RequestHeader = "X-Akira-Request";

    public const string Client = "web";

    public static CookieOptions CookieOptions(HttpRequest request, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        // Also when a proxy that is not trusted says the browser came over https: Secure is then right, and harmless if not
        Secure = CameOverHttps(request),
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true
    };

    public static bool ChangesState(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    /// <summary>
    /// Checks that a request carrying the cookie comes from a page of this server, by its Origin when the browser sends one.
    /// </summary>
    public static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CameOverHttps(HttpRequest request)
    {
        if (request.IsHttps)
        {
            return true;
        }

        var forwardedProto = request.Headers["X-Forwarded-Proto"].ToString();
        return forwardedProto.Contains("https", StringComparison.OrdinalIgnoreCase);
    }
}
