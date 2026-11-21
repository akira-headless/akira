using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class AuditEntry : BaseEntity
{
    // No foreign keys: the record stays when the user, device or item is deleted, which is when it matters most
    public Guid? UserId { get; set; }
    public required string UserName { get; set; }
    public Guid? DeviceId { get; set; }

    public ActionKind Action { get; set; }
    public ActionStatus Status { get; set; }
    public ActionScope? Scope { get; set; }

    public Guid? ItemId { get; set; }
    public required string IdentityKey { get; set; }
    public required string Title { get; set; }

    // The request and the outcome per instance, as JSON
    public required string Details { get; set; }
}
