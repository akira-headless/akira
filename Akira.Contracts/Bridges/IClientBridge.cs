namespace Akira.Contracts.Bridges;

/// <summary>
/// A compatibility API for clients that do not speak Akira's own. A plugin registers one as a singleton in
/// ConfigureServices and answers its clients with controllers that derive from BridgeController.
/// </summary>
public interface IClientBridge
{
    BridgeConnect Connect { get; }

    /// <summary>
    /// Builds the url a client installs for a new link on the bridge's base url as the user reached the server, without
    /// I/O, or returns null for a bridge that is not a Link bridge.
    /// </summary>
    BridgeLink? Link(Uri bridgeBase, string token);
}
