using System.Net;
using System.Security.Claims;
using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IPairingService
{
    /// <summary>
    /// Starts pairing a new device and returns the code it shows and the secret it polls with.
    /// </summary>
    Task<ServiceResult<PairingStarted>> StartAsync(StartPairingRequest request, IPAddress? from, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the state of a pairing, and the device token once it is approved. The token is handed out once.
    /// </summary>
    Task<ServiceResult<PairingPollResult>> PollAsync(Guid pairingId, string pollSecret, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingPairing>> ListPendingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Approves a pending code as a personal device of a user or as a shared device.
    /// </summary>
    Task<ServiceResult<DeviceSummary>> ApproveAsync(ClaimsPrincipal approver, ApprovePairingRequest request, CancellationToken cancellationToken);

    Task<ServiceResult<bool>> DenyAsync(string code, CancellationToken cancellationToken);
}
