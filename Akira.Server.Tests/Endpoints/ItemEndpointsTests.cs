using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests.Endpoints;

public abstract class ItemEndpointsTests
{
    protected abstract Task<(AkiraApiFactory Factory, IAsyncDisposable? Database)> CreateFactoryAsync(
        CancellationToken cancellationToken);

    [Fact]
    public async Task List_ByDefault_ReturnsAvailableMoviesAndSeriesByTitle()
    {
        await using var api = await StartAsync();

        using var body = await api.GetJsonAsync("/api/v1/items");

        Assert.Equal(["100% Wolf", "Heat", "Mayfair Witches"], Titles(body));
        Assert.Equal(3, body.RootElement.GetProperty("total").GetInt32());

        // Lists carry the overview, which Kodi lists and skin widgets show
        Assert.Equal("A group of professional bank robbers.", body.RootElement.GetProperty("items")[1].GetProperty("overview").GetString());
        Assert.Equal(949, body.RootElement.GetProperty("items")[1].GetProperty("externalIds").GetProperty("tmdb").GetInt32());
    }

    [Fact]
    public async Task List_IncludeUnavailable_AddsMissing()
    {
        await using var api = await StartAsync();

        using var body = await api.GetJsonAsync("/api/v1/items?includeUnavailable=true");

        Assert.Equal(["100% Wolf", "Alien", "Heat", "Mayfair Witches", "Missing Show"], Titles(body));
    }

    [Fact]
    public async Task List_ByType_AcceptsAnyCase()
    {
        await using var api = await StartAsync();

        using var lower = await api.GetJsonAsync("/api/v1/items?type=episode");
        using var upper = await api.GetJsonAsync("/api/v1/items?type=EPISODE");

        Assert.Equal(["The Witching Hour"], Titles(lower));
        Assert.Equal(Titles(lower), Titles(upper));
    }

    [Fact]
    public async Task List_SearchTreatsWildcardsLiterally()
    {
        await using var api = await StartAsync();

        using var percent = await api.GetJsonAsync("/api/v1/items?search=%25");
        using var heat = await api.GetJsonAsync("/api/v1/items?search=HEAT");

        Assert.Equal(["100% Wolf"], Titles(percent));
        Assert.Equal(["Heat"], Titles(heat));
    }

    [Fact]
    public async Task List_Search_IgnoresAccents()
    {
        await using var api = await StartAsync();
        using (var scope = api.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
            (await dbContext.MediaItems.FindAsync([api.Ids.Wolf], TestContext.Current.CancellationToken))!.Title = "Amélie";
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var plain = await api.GetJsonAsync("/api/v1/items?search=amelie");
        using var accented = await api.GetJsonAsync("/api/v1/items?search=Am%C3%A9lie");

        Assert.Equal(["Amélie"], Titles(plain));
        Assert.Equal(["Amélie"], Titles(accented));
    }

    [Fact]
    public async Task List_SortByYear_IsNewestFirst()
    {
        await using var api = await StartAsync();

        using var body = await api.GetJsonAsync("/api/v1/items?sort=year");

        Assert.Equal(["Mayfair Witches", "100% Wolf", "Heat"], Titles(body));
    }

    [Fact]
    public async Task List_SortByAdded_PutsASeriesByItsNewestEpisode()
    {
        await using var api = await StartAsync();
        using (var scope = api.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
            (await dbContext.MediaItems.FindAsync([api.Ids.Series], TestContext.Current.CancellationToken))!.AddedAt =
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
            (await dbContext.MediaItems.FindAsync([api.Ids.Episode1], TestContext.Current.CancellationToken))!.AddedAt =
                new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var body = await api.GetJsonAsync("/api/v1/items?sort=added&order=desc");

        Assert.Equal(["Mayfair Witches", "100% Wolf", "Heat"], Titles(body));
    }

    [Fact]
    public async Task Letters_PointAtWhereEachLetterStartsInTheTitleList()
    {
        await using var api = await StartAsync();

        using var letters = await api.GetJsonAsync("/api/v1/items/letters");
        using var list = await api.GetJsonAsync("/api/v1/items?sort=title");

        var entries = letters.RootElement.GetProperty("letters").EnumerateArray()
            .Select(entry => (entry.GetProperty("letter").GetString(), entry.GetProperty("offset").GetInt32(), entry.GetProperty("count").GetInt32()))
            .ToList();
        Assert.Equal([("#", 0, 1), ("H", 1, 1), ("M", 2, 1)], entries);
        Assert.Equal("Mayfair Witches", Titles(list)[2]);
    }

    [Fact]
    public async Task List_Pages()
    {
        await using var api = await StartAsync();

        using var first = await api.GetJsonAsync("/api/v1/items?limit=2");
        using var second = await api.GetJsonAsync("/api/v1/items?limit=2&offset=2");

        Assert.Equal(["100% Wolf", "Heat"], Titles(first));
        Assert.Equal(["Mayfair Witches"], Titles(second));
        Assert.Equal((3, 2, 2), (second.RootElement.GetProperty("total").GetInt32(),
            second.RootElement.GetProperty("offset").GetInt32(), second.RootElement.GetProperty("limit").GetInt32()));
    }

    [Fact]
    public async Task List_SummaryShape()
    {
        await using var api = await StartAsync();
        using (var scope = api.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
            (await dbContext.MediaItems.FindAsync([api.Ids.Heat], TestContext.Current.CancellationToken))!.ImdbRating = 8.3;
            // Two Radarrs hold the poster: one could not be blurred, the other's blur is the one shown
            var posters = dbContext.MediaImages.Where(image => image.MediaItemId == api.Ids.Heat && image.Kind == ImageKind.Poster).ToList();
            posters[0].BlurHash = string.Empty;
            posters[1].BlurHash = "LEHV6nWB2yk8pyo0adR*.7kCMdnj";
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var body = await api.GetJsonAsync("/api/v1/items?search=heat");
        var heat = body.RootElement.GetProperty("items")[0];

        Assert.Equal("movie", heat.GetProperty("type").GetString());
        Assert.Equal(1995, heat.GetProperty("year").GetInt32());
        Assert.True(heat.GetProperty("isAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, heat.GetProperty("seriesId").ValueKind);
        Assert.Equal(8.3, heat.GetProperty("imdbRating").GetDouble());
        Assert.Equal(JsonValueKind.Null, heat.GetProperty("rating").ValueKind);
        var poster = heat.GetProperty("images")[0];
        Assert.Equal("poster", poster.GetProperty("kind").GetString());
        Assert.Equal($"/api/v1/items/{api.Ids.Heat}/images/poster", poster.GetProperty("url").GetString());
        Assert.Equal("LEHV6nWB2yk8pyo0adR*.7kCMdnj", poster.GetProperty("blurHash").GetString());
    }

    [Fact]
    public async Task Get_Episode_CarriesSeriesAndInheritedLanguage()
    {
        await using var api = await StartAsync();

        using var body = await api.GetJsonAsync($"/api/v1/items/{api.Ids.Episode1}");
        var episode = body.RootElement;

        Assert.Equal("episode", episode.GetProperty("type").GetString());
        Assert.Equal(api.Ids.Season1, episode.GetProperty("parentId").GetGuid());
        Assert.Equal(api.Ids.Series, episode.GetProperty("seriesId").GetGuid());
        Assert.Equal("Mayfair Witches", episode.GetProperty("seriesTitle").GetString());
        Assert.Equal("eng", episode.GetProperty("originalLanguage").GetString());
        Assert.Equal("2023-01-01", episode.GetProperty("airDate").GetString());
        Assert.Equal("sonarr", episode.GetProperty("versions")[0].GetProperty("instance").GetString());
        Assert.Equal("still", episode.GetProperty("images")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Get_Series_ListsItsInstances_WithoutVersions()
    {
        await using var api = await StartAsync();

        using var body = await api.GetJsonAsync($"/api/v1/items/{api.Ids.Series}");

        Assert.Empty(body.RootElement.GetProperty("versions").EnumerateArray());
        var instance = Assert.Single(body.RootElement.GetProperty("instances").EnumerateArray());
        Assert.Equal("sonarr", instance.GetProperty("key").GetString());
    }

    [Fact]
    public async Task Get_MovieInTwoInstances_ListsBothVersionsInConfigurationOrder()
    {
        await using var api = await StartAsync();

        using var body = await api.GetJsonAsync($"/api/v1/items/{api.Ids.Heat}");
        var versions = body.RootElement.GetProperty("versions").EnumerateArray().ToList();

        Assert.Equal(["radarr", "radarr-4k"], versions.Select(version => version.GetProperty("instance").GetString()));
        Assert.Equal(["Radarr", "Radarr 4K"], versions.Select(version => version.GetProperty("instanceName").GetString()));
        Assert.Equal(60_000_000_000, versions[1].GetProperty("sizeBytes").GetInt64());
        Assert.Equal(["radarr", "radarr-4k"], body.RootElement.GetProperty("instances").EnumerateArray()
            .Select(instance => instance.GetProperty("key").GetString()));

        // The 4K version is probed and says what it is, the other one is still waiting
        Assert.Equal(["pending", "probed"], versions.Select(version => version.GetProperty("probeStatus").GetString()));
        Assert.Equal(JsonValueKind.Null, versions[0].GetProperty("video").ValueKind);
        var video = versions[1].GetProperty("video");
        Assert.Equal((3840, 2160, "hdr10"), (video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(),
            video.GetProperty("hdrFormat").GetString()));
        Assert.Equal(10_200_000, versions[1].GetProperty("durationMs").GetInt64());
        // Its tracks in their order, for the details a title's page shows
        Assert.Equal([("eng", "truehd", false), ("eng", "ac3", true)], versions[1].GetProperty("audio").EnumerateArray()
            .Select(track => (track.GetProperty("language").GetString(), track.GetProperty("codec").GetString(), track.GetProperty("isCommentary").GetBoolean())));
        Assert.Equal([("eng", true), ("dut", false)], versions[1].GetProperty("subtitles").EnumerateArray()
            .Select(track => (track.GetProperty("language").GetString(), track.GetProperty("isForced").GetBoolean())));
        Assert.Empty(versions[0].GetProperty("audio").EnumerateArray());
        Assert.All(versions, version => Assert.False(version.TryGetProperty("path", out _)));

        // Both instances supply a poster, the client sees one
        Assert.Equal(["poster"], body.RootElement.GetProperty("images").EnumerateArray()
            .Select(image => image.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task Get_MovieWithoutFile_HasNoVersions()
    {
        await using var api = await StartAsync();

        using var body = await api.GetJsonAsync($"/api/v1/items/{api.Ids.Alien}");

        Assert.Empty(body.RootElement.GetProperty("versions").EnumerateArray());
        Assert.False(body.RootElement.GetProperty("isAvailable").GetBoolean());
    }

    [Fact]
    public async Task Get_Unknown_IsNotFound()
    {
        await using var api = await StartAsync();

        var unknown = await api.Client.GetAsync($"/api/v1/items/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Children_OfSeries_AreAvailableSeasons()
    {
        await using var api = await StartAsync();

        using var available = await api.GetJsonAsync($"/api/v1/items/{api.Ids.Series}/children");
        using var all = await api.GetJsonAsync($"/api/v1/items/{api.Ids.Series}/children?includeUnavailable=true");

        Assert.Equal(["Season 1"], Titles(available));
        Assert.Equal(["Season 1", "Season 2"], Titles(all));
    }

    [Fact]
    public async Task Children_OfSeason_AreEpisodesInOrder()
    {
        await using var api = await StartAsync();

        using var all = await api.GetJsonAsync($"/api/v1/items/{api.Ids.Season1}/children?includeUnavailable=true");

        Assert.Equal(["The Witching Hour", "Second Sight"], Titles(all));
        Assert.Equal([1, 2], all.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("episodeNumber").GetInt32()));
    }

    [Theory]
    [InlineData("/api/v1/items?limit=0", "limit")]
    [InlineData("/api/v1/items?limit=201", "limit")]
    [InlineData("/api/v1/items?offset=-1", "offset")]
    public async Task List_InvalidPage_IsValidationProblem(string url, string field)
    {
        await using var api = await StartAsync();

        var response = await api.Client.GetAsync(url, TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    // Binding failures throw in Development. These are 400s, never the 500 the exception handler
    // turned them into before it was given a status code selector.
    [Theory]
    [InlineData("/api/v1/items?limit=abc")]
    [InlineData("/api/v1/items?type=7")]
    [InlineData("/api/v1/items?type=movie,series")]
    [InlineData("/api/v1/items?sort=rating")]
    public async Task List_UnbindableValue_IsBadRequest(string url)
    {
        await using var api = await StartAsync();

        var response = await api.Client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NextUp_AndUnplayedCounts_AreWorkedOutByTheDatabase()
    {
        await using var api = await StartAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        using (var scope = api.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
            dbContext.MediaFiles.Add(new MediaFile
            {
                MediaItemId = api.Ids.Episode2,
                InstanceKey = LibrarySeed.Sonarr.Key,
                Path = "/data/tv/Mayfair Witches/S01E02.mkv",
                SizeBytes = 1_000_000_000,
                ArrFileId = 2
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        async Task<int?> Unplayed(Guid id)
        {
            using var item = await api.GetJsonAsync($"/api/v1/items/{id}");
            return item.RootElement.GetProperty("unplayedCount").GetInt32();
        }

        async Task<List<string>> NextUp()
        {
            using var shelf = await api.GetJsonAsync("/api/v1/library/next-up");
            return shelf.RootElement.EnumerateArray().Select(item => item.GetProperty("title").GetString()!).ToList();
        }

        Assert.Equal((2, 2), (await Unplayed(api.Ids.Series), await Unplayed(api.Ids.Season1)));
        Assert.Empty(await NextUp());

        await api.Client.PostAsJsonAsync($"/api/v1/items/{api.Ids.Episode1}/actions/mark-played", new { played = true }, cancellationToken);
        Assert.Equal(["Second Sight"], await NextUp());
        Assert.Equal((1, 1), (await Unplayed(api.Ids.Series), await Unplayed(api.Ids.Season1)));

        // A season marked played gives its episodes one time, the last of them is where the user is
        await api.Client.PostAsJsonAsync($"/api/v1/items/{api.Ids.Season1}/actions/mark-played", new { played = true }, cancellationToken);
        Assert.Empty(await NextUp());
        using var series = await api.GetJsonAsync("/api/v1/items?type=series");
        Assert.Equal(0, series.RootElement.GetProperty("items")[0].GetProperty("unplayedCount").GetInt32());
    }

    private async Task<StartedApi> StartAsync()
    {
        var (factory, database) = await CreateFactoryAsync(TestContext.Current.CancellationToken);
        using var scope = factory.Services.CreateScope();
        var ids = await LibrarySeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AkiraDbContext>(),
            TestContext.Current.CancellationToken);
        return new StartedApi(factory, database, factory.CreateClient(), ids);
    }

    private static List<string> Titles(JsonDocument body) =>
        body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()!).ToList();

    protected sealed record StartedApi(AkiraApiFactory Factory, IAsyncDisposable? Database, HttpClient Client, LibrarySeed.Ids Ids)
        : IAsyncDisposable
    {
        public async Task<JsonDocument> GetJsonAsync(string url)
        {
            var response = await Client.GetAsync(url, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
            if (Database is not null)
            {
                await Database.DisposeAsync();
            }
        }
    }
}
