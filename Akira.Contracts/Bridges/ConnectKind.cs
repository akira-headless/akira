namespace Akira.Contracts.Bridges;

/// <summary>
/// Defines how a client of a bridge finds the server and signs in.
/// </summary>
public enum ConnectKind
{
    /// <summary>
    /// The user makes a link and gives the app its url, which carries the credential.
    /// </summary>
    Link,

    /// <summary>
    /// The user types the bridge's address in the app and approves the code it shows under Devices.
    /// </summary>
    Address,

    /// <summary>
    /// The app finds the server on the network by itself.
    /// </summary>
    Discovery
}
