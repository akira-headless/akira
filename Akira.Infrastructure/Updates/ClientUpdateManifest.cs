namespace Akira.Infrastructure.Updates;

/// <summary>
/// Represents the update.json a source publishes next to a build. File is a url, absolute or relative to the manifest;
/// Build is the number the app compares with its own.
/// </summary>
public sealed record ClientUpdateManifest(long Build, string? Version, string? File, string? Sha256);
