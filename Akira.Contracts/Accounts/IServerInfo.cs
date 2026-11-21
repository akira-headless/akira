namespace Akira.Contracts.Accounts;

/// <summary>
/// Defines what plugins can read about this server: its id and its addresses on the local network.
/// </summary>
public interface IServerInfo
{
    /// <summary>
    /// Returns an id of this server that stays the same across restarts and upgrades, for clients that tell servers apart.
    /// </summary>
    Task<Guid> ServerIdAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lists the http addresses the server answers on in the local network, one per IPv4 address of this machine, or
    /// none before it is listening.
    /// </summary>
    IReadOnlyList<Uri> Addresses();
}
