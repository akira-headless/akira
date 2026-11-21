using Akira.Contracts.Accounts;
using Akira.Contracts.Playback;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Actions;

namespace Akira.Server.Bridges;

public sealed class WatchStateAccess : IWatchStateAccess
{
    private readonly IActionService _actions;

    public WatchStateAccess(IActionService actions)
    {
        _actions = actions;
    }

    /// <inheritdoc />
    public async Task<bool> SetPlayedAsync(Caller caller, Guid itemId, bool played, CancellationToken cancellationToken)
    {
        var result = await _actions.MarkPlayedAsync(BridgePrincipal.For(caller), itemId, new MarkPlayedRequest(played), cancellationToken);
        return result.Succeeded;
    }
}
