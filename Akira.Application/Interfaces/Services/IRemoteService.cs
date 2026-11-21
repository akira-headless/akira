using Akira.Server.Models.Remote;

namespace Akira.Server.Interfaces.Services;

public interface IRemoteService
{
    /// <summary>
    /// Lists a user's other devices that are connected and take remote commands, with what each one plays.
    /// </summary>
    Task<IReadOnlyList<RemoteDevice>> ListAsync(Guid userId, Guid? ownDeviceId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a command to one of the user's connected devices, straight away and without storing it.
    /// </summary>
    Task<RemoteSendResult> SendAsync(Guid userId, Guid? fromDeviceId, Guid deviceId, RemoteCommandRequest command, CancellationToken cancellationToken);
}
