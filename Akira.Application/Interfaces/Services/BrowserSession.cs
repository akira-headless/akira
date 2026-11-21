using Akira.Server.Models.Auth;

namespace Akira.Server.Interfaces.Services;

/// <summary>
/// Represents a signed in browser. Token is the device token its session cookie holds, valid until ExpiresAt.
/// </summary>
public sealed record BrowserSession(string Token, DateTimeOffset ExpiresAt, CurrentUser User);
