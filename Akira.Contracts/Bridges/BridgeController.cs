using Microsoft.AspNetCore.Mvc;

namespace Akira.Contracts.Bridges;

/// <summary>
/// The base of the controllers of the bridge TBridge, answered under /bridges/{plugin key} while it is switched on, whatever
/// routes they declare. They are anonymous to the host and left out of the OpenAPI document, so the bridge decides who is
/// calling through IAccountAccess.
/// </summary>
public abstract class BridgeController<TBridge> : ControllerBase where TBridge : IClientBridge;
