using System.Net;

namespace Akira.Contracts.Accounts;

/// <summary>
/// Defines how plugins sign their clients in: checking tokens, pairing new devices and the fixed user of a bridge.
/// </summary>
public interface IAccountAccess
{
    /// <summary>
    /// Checks a link token and returns who it belongs to, or null when it is unknown, revoked or made for another bridge.
    /// </summary>
    Task<Caller?> AuthenticateLinkAsync(string bridgeKey, string token, CancellationToken cancellationToken);

    /// <summary>
    /// Checks a device token (a personal device or a profile session) and returns who it belongs to, or null when it is
    /// unknown, revoked, expired or a shared device without a profile.
    /// </summary>
    Task<Caller?> AuthenticateDeviceAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// Starts an ordinary pairing whose code someone approves in Akira under Devices, or returns null when that address
    /// started too many pairings or too many are waiting, the same limits as the API's own pairing start.
    /// </summary>
    Task<PairingStart?> StartPairingAsync(
        string deviceName,
        string client,
        string? clientVersion,
        string? platform,
        IPAddress? from,
        CancellationToken cancellationToken);

    /// <summary>
    /// Polls a pairing, which hands out the new device's token on the first poll after it is approved.
    /// </summary>
    Task<PairingPoll> PollPairingAsync(Guid pairingId, string pollSecret, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the caller a bridge acts as when its clients cannot sign in at all (DLNA), the user or profile by name
    /// through a link of this bridge made the first time, or null when no user has that name.
    /// </summary>
    Task<Caller?> FixedUserAsync(string bridgeKey, string userName, CancellationToken cancellationToken);
}
