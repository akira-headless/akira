using Akira.Core.Enum;

namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents the outcome of a delete or unmonitor, per instance. AuditId is null on a dry run, which is not recorded.
/// </summary>
public sealed record ActionResult(
    Guid? AuditId,
    ActionKind Action,
    ActionStatus Status,
    Guid ItemId,
    string Title,
    ActionScope Scope,
    IReadOnlyList<InstanceOutcome> Instances);
