namespace Akira.Infrastructure.Integrations.Radarr;

/// <summary>
/// Represents a rating as Radarr and Sonarr return it, a score out of 10 with its vote count.
/// </summary>
public sealed record ArrRating(int Votes, double Value);
