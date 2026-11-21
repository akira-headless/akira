using Akira.Core.Enum;

namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents the payload of the event sent when a delete or unmonitor finished. Instances are the keys of the
/// instances that acted.
/// </summary>
public sealed record ActionCompletedPayload(
    Guid AuditId,
    ActionKind Action,
    ActionStatus Status,
    Guid ItemId,
    ActionScope? Scope,
    IReadOnlyList<string> Instances);
