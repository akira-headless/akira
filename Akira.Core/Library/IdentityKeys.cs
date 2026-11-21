namespace Akira.Core.Library;

/// <summary>
/// Builds the key that identifies a logical item across syncs and instances.
/// </summary>
public static class IdentityKeys
{
    // An instance with one of these as its key would make fallback keys that read as a real TMDB or TVDB id
    public static readonly IReadOnlySet<string> ReservedInstanceKeys = new HashSet<string>(StringComparer.Ordinal) { "tmdb", "tvdb" };

    // Keyed on TMDB and TVDB rather than a Radarr or Sonarr id. The same movie in a 1080p and a 4K
    // Radarr is one item with two versions, and per user state keyed by this survives the item
    // being removed and added again. The instance id is only a fallback for the rare entry that
    // has no metadata id yet, scoped to its instance so two instances cannot collide.
    public static string Movie(int? tmdbId, string instanceKey, int radarrId) =>
        tmdbId > 0 ? $"movie:tmdb:{tmdbId}" : $"movie:{instanceKey}:{radarrId}";

    public static string Series(int? tvdbId, string instanceKey, int sonarrId) =>
        tvdbId > 0 ? $"series:tvdb:{tvdbId}" : $"series:{instanceKey}:{sonarrId}";

    public static string Season(string seriesKey, int seasonNumber) => $"{seriesKey}:s{seasonNumber}";

    public static string Episode(string seriesKey, int seasonNumber, int episodeNumber) =>
        $"{seriesKey}:s{seasonNumber}:e{episodeNumber}";
}
