using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface IPinSignInService
{
    /// <summary>
    /// Lists the users who can sign in with their PIN: the ones who set one.
    /// </summary>
    Task<IReadOnlyList<PinUser>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Signs a new device in as a user who gives their PIN, as the personal device a pairing they approved would make.
    /// </summary>
    Task<ServiceResult<PairingCompleted>> SignInAsync(PinSignInRequest request, CancellationToken cancellationToken);
}
