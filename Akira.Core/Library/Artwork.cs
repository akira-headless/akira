using System.Text.RegularExpressions;
using Akira.Core.Enum;

namespace Akira.Core.Library;

/// <summary>
/// Validates artwork references received from Radarr and Sonarr.
/// </summary>
public static partial class Artwork
{
    private const string TmdbOriginal = "https://image.tmdb.org/t/p/original/";

    // Image URLs arrive in an upstream response, so they are only ever fetched from hosts on this
    // list. Anything else is dropped at sync time and refused again when the image is served.
    public static readonly IReadOnlySet<string> AllowedRemoteHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image.tmdb.org",
        "artworks.thetvdb.com",
        "thetvdb.com",
        "assets.fanart.tv"
    };

    public static ImageKind? ParseCoverType(string? coverType) => coverType?.ToLowerInvariant() switch
    {
        "poster" => ImageKind.Poster,
        "fanart" => ImageKind.Fanart,
        "banner" => ImageKind.Banner,
        "clearlogo" => ImageKind.Logo,
        "screenshot" => ImageKind.Still,
        _ => null
    };

    /// <summary>
    /// Extracts the file name from a MediaCover url like /MediaCover/12/poster.jpg?lastWrite=1.
    /// </summary>
    public static string? ParseArrFileName(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var match = MediaCoverUrl().Match(url);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["file"].Value.ToLowerInvariant();
    }

    public static string? ValidateRemoteUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !AllowedRemoteHosts.Contains(uri.Host))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    public static bool IsArrFileName(string fileName) => ArrFileName().IsMatch(fileName);

    /// <summary>
    /// Gets the name of Radarr's or Sonarr's resized copy of a MediaCover file, or null when they keep none at that size.
    /// </summary>
    public static string? SizedArrFileName(string fileName, ImageKind kind, ImageSize size)
    {
        // The heights Servarr's MediaCoverService resizes to; it never resizes logos
        int? height = (kind, size) switch
        {
            (ImageKind.Poster, ImageSize.Medium) => 500,
            (ImageKind.Poster, ImageSize.Small) => 250,
            (ImageKind.Fanart or ImageKind.Still, ImageSize.Medium) => 360,
            (ImageKind.Fanart or ImageKind.Still, ImageSize.Small) => 180,
            (ImageKind.Banner, ImageSize.Medium) => 70,
            (ImageKind.Banner, ImageSize.Small) => 35,
            _ => null
        };

        var match = ArrFileParts().Match(fileName);
        if (height is null || !match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value;
        var extension = match.Groups["extension"].Value;
        return $"{name}-{height}{extension}";
    }

    /// <summary>
    /// Gets a smaller rendition of a TMDB image url, or null for any other host or size.
    /// </summary>
    public static string? SizedRemoteUrl(string url, ImageKind kind, ImageSize size)
    {
        var width = (kind, size) switch
        {
            (_, ImageSize.Original) => null,
            (ImageKind.Fanart or ImageKind.Still, ImageSize.Medium) => "w780",
            (ImageKind.Fanart or ImageKind.Still, ImageSize.Small) => "w300",
            (_, ImageSize.Medium) => "w500",
            _ => "w342"
        };

        if (width is null || !url.StartsWith(TmdbOriginal, StringComparison.Ordinal))
        {
            return null;
        }

        var path = url[TmdbOriginal.Length..];
        return $"https://image.tmdb.org/t/p/{width}/{path}";
    }

    [GeneratedRegex(@"^(?:/[^/?#]+)*/MediaCover/\d+/(?<file>[a-z]+(?:-\d+)?\.(?:jpg|jpeg|png|webp))(?:\?.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex MediaCoverUrl();

    [GeneratedRegex(@"^[a-z]+(?:-\d+)?\.(?:jpg|jpeg|png|webp)$")]
    private static partial Regex ArrFileName();

    [GeneratedRegex(@"^(?<name>[a-z]+)(?:-\d+)?(?<extension>\.(?:jpg|jpeg|png|webp))$")]
    private static partial Regex ArrFileParts();
}
