using Akira.Core.Library;
using Xunit;

namespace Akira.Core.Tests.Library;

public class PathMapperTests
{
    [Theory]
    [InlineData("/movies/Heat (1995)/Heat.mkv", "/data/media/movies/Heat (1995)/Heat.mkv")]
    [InlineData("/movies", "/data/media/movies")]
    [InlineData("/tv/Show/S01E01.mkv", "/tv/Show/S01E01.mkv")]
    public void Map_ReplacesMatchingPrefix(string path, string expected)
    {
        var mapper = new PathMapper([new PathMapping("/movies", "/data/media/movies")]);

        Assert.Equal(expected, mapper.Map(path));
    }

    [Fact]
    public void Map_MatchesWholeSegmentsOnly()
    {
        var mapper = new PathMapper([new PathMapping("/movies", "/data/movies")]);

        Assert.Equal("/movies-4k/Heat.mkv", mapper.Map("/movies-4k/Heat.mkv"));
    }

    [Fact]
    public void Map_PrefersLongestPrefix()
    {
        var mapper = new PathMapper(
        [
            new PathMapping("/media", "/data"),
            new PathMapping("/media/anime", "/anime")
        ]);

        Assert.Equal("/anime/Show/S01E01.mkv", mapper.Map("/media/anime/Show/S01E01.mkv"));
        Assert.Equal("/data/tv/Show/S01E01.mkv", mapper.Map("/media/tv/Show/S01E01.mkv"));
    }

    [Fact]
    public void Map_IgnoresTrailingSeparatorsInConfiguration()
    {
        var mapper = new PathMapper([new PathMapping("/movies/", "/data/movies/")]);

        Assert.Equal("/data/movies/Heat.mkv", mapper.Map("/movies/Heat.mkv"));
    }

    [Fact]
    public void Map_WindowsSourceToLinuxTarget_ConvertsSeparatorsAndIgnoresCase()
    {
        var mapper = new PathMapper([new PathMapping(@"D:\Media\Movies", "/data/movies")]);

        Assert.Equal("/data/movies/Heat (1995)/Heat.mkv", mapper.Map(@"d:\media\movies\Heat (1995)\Heat.mkv"));
    }

    [Fact]
    public void Map_LinuxSourceToWindowsTarget_ConvertsSeparators()
    {
        var mapper = new PathMapper([new PathMapping("/movies", @"\\nas\media\movies")]);

        Assert.Equal(@"\\nas\media\movies\Heat (1995)\Heat.mkv", mapper.Map("/movies/Heat (1995)/Heat.mkv"));
    }

    [Fact]
    public void Map_LinuxPaths_AreCaseSensitive()
    {
        var mapper = new PathMapper([new PathMapping("/Movies", "/data/movies")]);

        Assert.Equal("/movies/Heat.mkv", mapper.Map("/movies/Heat.mkv"));
    }

    [Fact]
    public void Map_ToAndFromTheRoot_JoinsWithOneSeparator()
    {
        // A container that mounts the media share at its root, like a translator seeing /movies where the server has /data/media/movies
        var toRoot = new PathMapper([new PathMapping("/data/media", "/")]);
        var fromRoot = new PathMapper([new PathMapping("/", "/data/media")]);

        Assert.Equal("/movies/Heat.en.srt", toRoot.Map("/data/media/movies/Heat.en.srt"));
        Assert.Equal("/data/media/movies/Heat.nl.srt", fromRoot.Map("/movies/Heat.nl.srt"));
        Assert.False(toRoot.TryMap("/data/mediathek/Heat.mkv", out _));
        Assert.False(fromRoot.TryMap("relative/Heat.mkv", out _));
    }

    [Fact]
    public void None_ReturnsPathUnchanged()
    {
        Assert.Equal("/movies/Heat.mkv", PathMapper.None.Map("/movies/Heat.mkv"));
    }
}
