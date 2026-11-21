namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a new link for a bridge. Token is shown once. Url is what the app installs, with the token in it, and
/// AppUrl opens it in the app.
/// </summary>
public sealed record LinkCreated(DeviceSummary Device, string Bridge, string Token, string Url, string? AppUrl);
