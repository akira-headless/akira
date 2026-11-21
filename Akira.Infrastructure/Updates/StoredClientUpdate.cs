namespace Akira.Infrastructure.Updates;

/// <summary>
/// Represents the build of a platform the server holds, with the full path of its file.
/// </summary>
public sealed record StoredClientUpdate(string Platform, long Build, string Version, string FileName, string Path, long SizeBytes, string Sha256,
    DateTimeOffset FetchedAt);
