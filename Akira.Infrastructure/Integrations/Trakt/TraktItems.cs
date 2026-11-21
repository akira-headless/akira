using System.Globalization;
using System.Text.RegularExpressions;

namespace Akira.Infrastructure.Integrations.Trakt;

/// <summary>
/// Maps identity keys to what Trakt knows them by, and writes the bodies Trakt's scrobble and history calls take.
/// </summary>
public static partial class TraktItems
{
    /// <summary>
    /// Reads a movie or episode identity key. Keys built from a Radarr or Sonarr id, without TMDB or TVDB, mean nothing to Trakt.
    /// </summary>
    public static TraktItem? FromIdentityKey(string identityKey)
    {
        var movie = MoviePattern().Match(identityKey);
        if (movie.Success)
        {
            return new TraktItem(Number(movie, 1), null, null, null);
        }

        var episode = EpisodePattern().Match(identityKey);
        return episode.Success ? new TraktItem(null, Number(episode, 1), Number(episode, 2), Number(episode, 3)) : null;
    }

    public static string MovieKey(int tmdb) => $"movie:tmdb:{tmdb.ToString(CultureInfo.InvariantCulture)}";

    public static string EpisodeKey(int tvdb, int season, int episode) =>
        string.Create(CultureInfo.InvariantCulture, $"series:tvdb:{tvdb}:s{season}:e{episode}");

    public static object ScrobbleBody(TraktItem item, double progress)
    {
        var rounded = Math.Round(Math.Clamp(progress, 0, 100), 2);
        if (item.IsMovie)
        {
            return new { movie = Ids(item), progress = rounded };
        }

        var show = new { ids = new { tvdb = item.Tvdb } };
        var episode = new { season = item.Season, number = item.Episode };
        return new { show, episode, progress = rounded };
    }

    /// <summary>
    /// Writes a /sync/history body: movies by TMDB id, episodes grouped under their show and season, each with when it was watched.
    /// </summary>
    public static object HistoryBody(IEnumerable<(TraktItem Item, DateTimeOffset WatchedAt)> items)
    {
        var list = items.ToList();
        var movies = list
            .Where(entry => entry.Item.IsMovie)
            .Select(entry => new { ids = new { tmdb = entry.Item.Tmdb }, watched_at = entry.WatchedAt })
            .ToList();
        var shows = list
            .Where(entry => !entry.Item.IsMovie)
            .GroupBy(entry => entry.Item.Tvdb)
            .Select(show =>
            {
                var ids = new { tvdb = show.Key };
                var seasons = show
                    .GroupBy(entry => entry.Item.Season)
                    .Select(season =>
                    {
                        var episodes = season.Select(entry => new { number = entry.Item.Episode, watched_at = entry.WatchedAt }).ToList();
                        return new { number = season.Key, episodes };
                    })
                    .ToList();
                return new { ids, seasons };
            })
            .ToList();
        return new { movies, shows };
    }

    /// <summary>
    /// Writes a /sync/history/remove body, the same grouping without times: every play of each item goes.
    /// </summary>
    public static object RemoveBody(IEnumerable<TraktItem> items)
    {
        var list = items.ToList();
        var movies = list
            .Where(item => item.IsMovie)
            .Select(item => new { ids = new { tmdb = item.Tmdb } })
            .ToList();
        var shows = list
            .Where(item => !item.IsMovie)
            .GroupBy(item => item.Tvdb)
            .Select(show =>
            {
                var ids = new { tvdb = show.Key };
                var seasons = show
                    .GroupBy(item => item.Season)
                    .Select(season =>
                    {
                        var episodes = season.Select(item => new { number = item.Episode }).ToList();
                        return new { number = season.Key, episodes };
                    })
                    .ToList();
                return new { ids, seasons };
            })
            .ToList();
        return new { movies, shows };
    }

    private static object Ids(TraktItem item) => new { ids = new { tmdb = item.Tmdb } };

    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^movie:tmdb:(\d+)$")]
    private static partial Regex MoviePattern();

    [GeneratedRegex(@"^series:tvdb:(\d+):s(\d+):e(\d+)$")]
    private static partial Regex EpisodePattern();
}
