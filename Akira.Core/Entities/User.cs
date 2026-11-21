using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class User : BaseEntity
{
    public required string Name { get; set; }

    // Unique in the database. The case-insensitive collation of MariaDB and the case-sensitive
    // one of SQLite would otherwise disagree on whether "Rowan" and "rowan" are the same name.
    public required string NormalizedName { get; set; }

    public UserRole Role { get; set; } = UserRole.User;
    public string? PinHash { get; set; }

    public List<Device> Devices { get; set; } = [];
    public List<ApiKey> ApiKeys { get; set; } = [];
}
