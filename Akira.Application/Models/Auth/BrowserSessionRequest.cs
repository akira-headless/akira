namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to sign a browser in. Name and Platform describe a browser signing in with an API
/// key, which becomes a new device. A paired browser keeps its own.
/// </summary>
public sealed record BrowserSessionRequest(string? Name, string? Platform);
