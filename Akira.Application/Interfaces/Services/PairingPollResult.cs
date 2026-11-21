using Akira.Server.Models.Auth;

namespace Akira.Server.Interfaces.Services;

/// <summary>
/// Represents the answer to a pairing poll. Pending is set while the pairing waits for approval, Completed once the
/// token is handed out.
/// </summary>
public sealed record PairingPollResult(PairingPending? Pending, PairingCompleted? Completed);
