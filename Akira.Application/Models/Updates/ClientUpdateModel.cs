namespace Akira.Server.Models.Updates;

/// <summary>
/// Represents the newest build of a client app the server holds. An app installs it when Build is higher than its own.
/// </summary>
public sealed record ClientUpdateModel(
    string Platform,
    long Build,
    string Version,
    string FileName,
    long SizeBytes,
    string Sha256,
    DateTimeOffset FetchedAt,
    string DownloadUrl);
