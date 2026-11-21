namespace Akira.Infrastructure.Integrations.Radarr;

/// <summary>
/// Represents the file of a movie as Radarr returns it. Path is as Radarr sees it, before the path mappings.
/// </summary>
public sealed record RadarrMovieFile(int Id, string? Path, long Size, DateTimeOffset? DateAdded);
