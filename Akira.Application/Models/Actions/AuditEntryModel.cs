using System.Text.Json;
using Akira.Core.Enum;

namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents one entry of the audit log. Details is the request and the outcome per instance as JSON, and the entry
/// stays when its user, device or item is deleted.
/// </summary>
public sealed record AuditEntryModel(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid? UserId,
    string UserName,
    Guid? DeviceId,
    ActionKind Action,
    ActionStatus Status,
    ActionScope? Scope,
    Guid? ItemId,
    string IdentityKey,
    string Title,
    JsonElement Details);
