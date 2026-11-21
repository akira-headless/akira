using Akira.Contracts.Bridges;

namespace Akira.Server.Tests.Plugins;

public sealed class ProbeBridge : IClientBridge
{
    public BridgeConnect Connect { get; } = new(ConnectKind.Address, "A bridge for tests.");

    public BridgeLink? Link(Uri bridgeBase, string token) => null;
}
