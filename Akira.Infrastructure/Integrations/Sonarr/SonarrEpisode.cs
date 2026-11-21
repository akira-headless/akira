using Akira.Infrastructure.Integrations.Arr;

namespace Akira.Infrastructure.Integrations.Sonarr;

/// <summary>
/// Represents an episode as Sonarr's API returns it.
/// </summary>
public sealed record SonarrEpisode(
    int Id,
    int SeriesId,
    int? TvdbId,
    int SeasonNumber,
    int EpisodeNumber,
    string? Title,
    string? Overview,
    DateOnly? AirDate,
    int? Runtime,
    bool HasFile,
    int? EpisodeFileId,
    List<ArrImage>? Images);
