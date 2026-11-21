namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents a switched on bridge and how its app connects. Address is the bridge's base url as the caller reached the
/// server, what an Address bridge's app is given.
/// </summary>
public sealed record BridgeSummary(string Key, string Name, string Description, BridgeConnectKind Connect, string Instructions, string Address);
