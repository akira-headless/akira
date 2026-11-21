using Akira.Core.Library;
using Xunit;

namespace Akira.Core.Tests.Library;

public class LibraryPathsTests
{
    [Theory]
    [InlineData("/movies/Heat (1995)/Heat.mkv", "/data/movies/Heat (1995)/Heat.mkv")]
    [InlineData("/movies/../app/config/akira.db", null)]
    [InlineData("/app/config/keys/key.xml", null)]
    [InlineData("movies/Heat.mkv", null)]
    public void WithPathMappings_OnlyAFileUnderOneIsTaken(string arrPath, string? expected)
    {
        var paths = new LibraryPaths(new PathMapper([new PathMapping("/movies", "/data/movies")]), requireMapping: true, []);

        Assert.Equal(expected, paths.Resolve(arrPath));
    }

    [Theory]
    [InlineData("/data/movies/Heat.mkv", "/data/movies/Heat.mkv")]
    [InlineData("/app/config/akira.db", null)]
    [InlineData("/proc/self/environ", null)]
    [InlineData("//app/config/akira.db", null)]
    [InlineData("/data//etc/shadow", null)]
    [InlineData(@"\\nas\media\Heat.mkv", @"\\nas\media\Heat.mkv")]
    [InlineData(@"C:\Akira\config\keys\key.xml", null)]
    [InlineData(@"C:\Akira-media\Heat.mkv", @"C:\Akira-media\Heat.mkv")]
    public void WithoutPathMappings_TheServersOwnFoldersStayOut(string arrPath, string? expected)
    {
        var paths = new LibraryPaths(PathMapper.None, requireMapping: false, ["/app", @"C:\Akira"]);

        Assert.Equal(expected, paths.Resolve(arrPath));
    }
}
