namespace Akira.Infrastructure.Integrations.Sonarr;

/// <summary>
/// Represents an episode file as Sonarr returns it. Path is as Sonarr sees it, before the path mappings.
/// </summary>
public sealed record SonarrEpisodeFile(int Id, int SeriesId, string? Path, long Size, DateTimeOffset? DateAdded);
