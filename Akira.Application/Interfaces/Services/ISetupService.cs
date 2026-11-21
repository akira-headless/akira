using Akira.Server.Models.Auth;
using Akira.Server.Services;

namespace Akira.Server.Interfaces.Services;

public interface ISetupService
{
    /// <summary>
    /// Reads whether the first admin exists and logs the setup code when it does not.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    SetupStatus GetStatus();

    /// <summary>
    /// Creates the first admin with the setup code from the log, and an API key for it.
    /// </summary>
    Task<ServiceResult<SetupResponse>> CompleteAsync(SetupRequest request, CancellationToken cancellationToken);
}
