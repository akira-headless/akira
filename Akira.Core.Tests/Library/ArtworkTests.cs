using Akira.Core.Enum;
using Akira.Core.Library;
using Xunit;

namespace Akira.Core.Tests.Library;

public class ArtworkTests
{
    [Theory]
    [InlineData("/MediaCover/1/poster.jpg?lastWrite=639221538113720912", "poster.jpg")]
    [InlineData("/MediaCover/12/clearlogo.png", "clearlogo.png")]
    [InlineData("/radarr/MediaCover/3/fanart-360.jpg?lastWrite=1", "fanart-360.jpg")]
    [InlineData("/MediaCover/3/Poster.JPG", "poster.jpg")]
    public void ParseArrFileName_ExtractsFileName(string url, string expected)
    {
        Assert.Equal(expected, Artwork.ParseArrFileName(url));
    }

    [Theory]
    [InlineData("https://image.tmdb.org/t/p/original/a.jpg")]
    [InlineData("/MediaCover/1/../../config.xml")]
    [InlineData("/MediaCover/1/poster.exe")]
    [InlineData("/api/v3/system/status")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseArrFileName_RejectsAnythingElse(string? url)
    {
        Assert.Null(Artwork.ParseArrFileName(url));
    }

    [Theory]
    [InlineData("https://image.tmdb.org/t/p/original/sQckQRt17VaWbo39GIu0TMOiszq.jpg")]
    [InlineData("https://artworks.thetvdb.com/banners/v4/episode/8884747/screencap/62c94928b91d8.jpg")]
    public void ValidateRemoteUrl_AcceptsAllowedHostsOverHttps(string url)
    {
        Assert.Equal(url, Artwork.ValidateRemoteUrl(url));
    }

    [Theory]
    [InlineData("http://image.tmdb.org/t/p/original/a.jpg")]
    [InlineData("https://image.tmdb.org.evil.example/a.jpg")]
    [InlineData("https://evil.example/a.jpg")]
    [InlineData("https://user:pass@image.tmdb.org/a.jpg")]
    [InlineData("https://image.tmdb.org:8443/a.jpg")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("/MediaCover/1/poster.jpg")]
    [InlineData(null)]
    public void ValidateRemoteUrl_RejectsEverythingElse(string? url)
    {
        Assert.Null(Artwork.ValidateRemoteUrl(url));
    }

    [Theory]
    [InlineData("poster", ImageKind.Poster)]
    [InlineData("fanart", ImageKind.Fanart)]
    [InlineData("banner", ImageKind.Banner)]
    [InlineData("clearlogo", ImageKind.Logo)]
    [InlineData("screenshot", ImageKind.Still)]
    [InlineData("headshot", null)]
    [InlineData(null, null)]
    public void ParseCoverType_MapsKnownTypes(string? coverType, ImageKind? expected)
    {
        Assert.Equal(expected, Artwork.ParseCoverType(coverType));
    }

    [Theory]
    [InlineData("poster.jpg", ImageKind.Poster, ImageSize.Medium, "poster-500.jpg")]
    [InlineData("poster-500.jpg", ImageKind.Poster, ImageSize.Small, "poster-250.jpg")]
    [InlineData("fanart.jpg", ImageKind.Fanart, ImageSize.Medium, "fanart-360.jpg")]
    [InlineData("clearlogo.png", ImageKind.Logo, ImageSize.Small, null)]
    [InlineData("poster.jpg", ImageKind.Poster, ImageSize.Original, null)]
    public void SizedArrFileName_NamesServarrsResizedCopy(string fileName, ImageKind kind, ImageSize size, string? expected)
    {
        Assert.Equal(expected, Artwork.SizedArrFileName(fileName, kind, size));
    }

    [Theory]
    [InlineData("https://image.tmdb.org/t/p/original/heat.jpg", ImageKind.Poster, ImageSize.Medium, "https://image.tmdb.org/t/p/w500/heat.jpg")]
    [InlineData("https://image.tmdb.org/t/p/original/still.jpg", ImageKind.Still, ImageSize.Small, "https://image.tmdb.org/t/p/w300/still.jpg")]
    [InlineData("https://artworks.thetvdb.com/banners/v4/episode/1/screencap/a.jpg", ImageKind.Still, ImageSize.Small, null)]
    [InlineData("https://image.tmdb.org/t/p/original/heat.jpg", ImageKind.Poster, ImageSize.Original, null)]
    public void SizedRemoteUrl_OnlyResizesTmdbOriginals(string url, ImageKind kind, ImageSize size, string? expected)
    {
        Assert.Equal(expected, Artwork.SizedRemoteUrl(url, kind, size));
    }
}
