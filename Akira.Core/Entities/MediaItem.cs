using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class MediaItem : BaseEntity
{
    public const int ImdbIdLength = 20;
    public const int CertificationLength = 20;

    public MediaItemType Type { get; set; }
    public Guid? ParentId { get; set; }
    public MediaItem? Parent { get; set; }
    public List<MediaItem> Children { get; set; } = [];

    public required string IdentityKey { get; set; }
    public required string Title { get; set; }
    public required string SortTitle { get; set; }

    // The title folded for search, kept up to date by the database context on every save
    public string? SearchTitle { get; set; }
    public int? Year { get; set; }
    public string? Overview { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public DateOnly? AirDate { get; set; }
    public int? RuntimeMinutes { get; set; }

    public int? TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? OriginalLanguage { get; set; }

    // From Radarr (IMDb and TMDB) or Sonarr (its own rating); scores out of 10
    public double? ImdbRating { get; set; }
    public int? ImdbVotes { get; set; }
    public double? TmdbRating { get; set; }
    public double? Rating { get; set; }
    public string? TrailerYoutubeId { get; set; }

    // Comma separated, as the instance lists them
    public string? Genres { get; set; }
    public string? Certification { get; set; }
    public DateTimeOffset? AddedAt { get; set; }

    public List<ArrLink> Links { get; set; } = [];
    public List<MediaFile> Files { get; set; } = [];
    public List<MediaImage> Images { get; set; } = [];
}
