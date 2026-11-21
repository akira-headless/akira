namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to make a link for one bridge. Name defaults to the bridge's name.
/// </summary>
public sealed record CreateLinkRequest(string Bridge, string? Name);
