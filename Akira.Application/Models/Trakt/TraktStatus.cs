namespace Akira.Server.Models.Trakt;

/// <summary>
/// Represents the Trakt link of a user. Configured says whether this server has a Trakt app at all, and Pending is set
/// while a link waits for the user on Trakt's site.
/// </summary>
public sealed record TraktStatus(
    bool Configured,
    bool Linked,
    string? Username,
    DateTimeOffset? LinkedAt,
    DateTimeOffset? LastImportAt,
    DateTimeOffset? LastSyncedAt,
    string? LastError,
    TraktPending? Pending);
