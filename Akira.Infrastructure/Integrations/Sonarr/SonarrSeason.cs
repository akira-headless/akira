using Akira.Infrastructure.Integrations.Arr;

namespace Akira.Infrastructure.Integrations.Sonarr;

/// <summary>
/// Represents a season of a series as Sonarr returns it, with its own images.
/// </summary>
public sealed record SonarrSeason(int SeasonNumber, List<ArrImage>? Images);
