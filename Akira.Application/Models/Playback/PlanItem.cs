using Akira.Core.Enum;

namespace Akira.Server.Models.Playback;

/// <summary>
/// Represents the movie or episode a playback is for, with the series title for an episode.
/// </summary>
public sealed record PlanItem(Guid Id, MediaItemType Type, string Title, string? SeriesTitle, int? SeasonNumber, int? EpisodeNumber);
