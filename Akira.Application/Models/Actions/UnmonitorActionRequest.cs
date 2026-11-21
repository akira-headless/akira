using Akira.Core.Enum;

namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents the body of a request to unmonitor an item and keep its files. Scope and Instances are both required.
/// </summary>
public sealed record UnmonitorActionRequest(ActionScope? Scope, IReadOnlyList<string>? Instances, bool DryRun = false);
