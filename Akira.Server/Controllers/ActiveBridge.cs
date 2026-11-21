using System.Reflection;

namespace Akira.Server.Controllers;

/// <summary>
/// Represents a switched on bridge. Bridge is the class the plugin registered as its IClientBridge, Assembly the
/// plugin's own.
/// </summary>
public sealed record ActiveBridge(string Key, Type Bridge, Assembly Assembly);
