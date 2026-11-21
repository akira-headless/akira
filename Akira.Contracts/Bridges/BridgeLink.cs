namespace Akira.Contracts.Bridges;

/// <summary>
/// Represents the url a client installs for a link. AppUrl opens it in the app itself when one is installed, with the app's
/// own url scheme.
/// </summary>
public sealed record BridgeLink(string Url, string? AppUrl);
