namespace Akira.Contracts.Accounts;

/// <summary>
/// Defines the states of a pairing as its bridge polls it.
/// </summary>
public enum PairingState
{
    /// <summary>
    /// Nobody approved the code yet.
    /// </summary>
    Waiting,

    /// <summary>
    /// Approved. Token is the new device's credential, handed out this once.
    /// </summary>
    Approved,

    /// <summary>
    /// Denied, expired, unknown, or the token was handed out already.
    /// </summary>
    Gone
}
