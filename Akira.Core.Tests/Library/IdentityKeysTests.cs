using Akira.Core.Library;
using Xunit;

namespace Akira.Core.Tests.Library;

public class IdentityKeysTests
{
    [Fact]
    public void Movie_UsesTmdbId_SoEveryInstanceAndRadarrIdIsTheSameItem()
    {
        Assert.Equal(IdentityKeys.Movie(170, "radarr", 1), IdentityKeys.Movie(170, "radarr-4k", 42));
        Assert.Equal("movie:tmdb:170", IdentityKeys.Movie(170, "radarr", 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Movie_WithoutTmdbId_FallsBackToIdWithinItsInstance(int? tmdbId)
    {
        Assert.Equal("movie:radarr-4k:7", IdentityKeys.Movie(tmdbId, "radarr-4k", 7));
        Assert.NotEqual(IdentityKeys.Movie(tmdbId, "radarr", 7), IdentityKeys.Movie(tmdbId, "radarr-4k", 7));
    }

    [Fact]
    public void SeasonsAndEpisodes_AreScopedToTheirSeries()
    {
        var series = IdentityKeys.Series(267441, "sonarr", 6);

        Assert.Equal("series:tvdb:267441", series);
        Assert.Equal("series:tvdb:267441:s0", IdentityKeys.Season(series, 0));
        Assert.Equal("series:tvdb:267441:s2:e13", IdentityKeys.Episode(series, 2, 13));
    }

    [Fact]
    public void Series_WithoutTvdbId_FallsBackToIdWithinItsInstance()
    {
        Assert.Equal("series:sonarr-anime:6", IdentityKeys.Series(null, "sonarr-anime", 6));
    }
}
