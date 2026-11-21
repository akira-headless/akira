namespace Akira.Core.Entities;

// A user's Trakt account. The tokens are protected with ASP.NET Data Protection, the server has to send them.
public class TraktLink : BaseEntity
{
    public Guid UserId { get; set; }
    public required string Username { get; set; }
    public required string ProtectedAccessToken { get; set; }
    public required string ProtectedRefreshToken { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? LastImportAt { get; set; }
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
}
