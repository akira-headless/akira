namespace Akira.Contracts.Bridges;

/// <summary>
/// Represents how a client connects to a bridge, shown under Devices in the web UI. Instructions is one or two sentences
/// for the user.
/// </summary>
public sealed record BridgeConnect(ConnectKind Kind, string Instructions);
