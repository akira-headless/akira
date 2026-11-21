using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class RemovalSetting : BaseEntity
{
    public const string ServerKey = "server";

    // "server" for the default, the user id for an override. A plain unique key, since a unique
    // index treats every empty user id as different and would allow two server rows.
    public required string ScopeKey { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    // Empty means the next level decides: the server default, then delete and unmonitor
    public RemovalBehavior? Movie { get; set; }
    public RemovalBehavior? Episode { get; set; }
    public RemovalBehavior? Season { get; set; }
    public RemovalBehavior? Series { get; set; }

    // Only on the server row. Admins can always remove.
    public bool? UsersMayRemove { get; set; }
}
