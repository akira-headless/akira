using Akira.Infrastructure.Integrations.Arr;

namespace Akira.Infrastructure.Sync;

/// <summary>
/// Represents what Radarr says about a movie and Sonarr about a series in the same words.
/// </summary>
internal sealed record ItemDescription(
    string Title,
    string? SortTitle,
    int? Year,
    string? Overview,
    int? Runtime,
    int? TmdbId,
    string? ImdbId,
    ArrLanguage? OriginalLanguage,
    IReadOnlyList<string>? Genres,
    string? Certification);
