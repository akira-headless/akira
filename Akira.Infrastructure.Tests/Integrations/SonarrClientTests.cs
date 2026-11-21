using System.Net;
using System.Text.Json.Nodes;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Sonarr;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Integrations;

public class SonarrClientTests
{
    [Fact]
    public async Task GetSeries_ReadsRealResponse()
    {
        var series = await CreateClient(StubHttpHandler.Json(Fixtures.Read("sonarr-series.json")))
            .GetSeriesAsync(TestContext.Current.CancellationToken);

        var anime = Assert.Single(series);
        Assert.Equal(267441, anime.TvdbId);
        Assert.Equal("Japanese", anime.OriginalLanguage?.Name);
        Assert.Equal([0, 1, 2], anime.Seasons!.Select(season => season.SeasonNumber));
    }

    [Fact]
    public async Task GetEpisodes_ReadsRealResponseWithImages()
    {
        var handler = StubHttpHandler.Json(Fixtures.Read("sonarr-episodes-6.json"));

        var episodes = await CreateClient(handler).GetEpisodesAsync(6, TestContext.Current.CancellationToken);

        Assert.Equal("http://sonarr:8989/api/v3/episode?seriesId=6&includeImages=true",
            Assert.Single(handler.Requests).RequestUri!.ToString());
        Assert.Equal(64, episodes.Count);
        var episode = episodes.Single(e => e.Id == 256);
        Assert.Equal((2, 1), (episode.SeasonNumber, episode.EpisodeNumber));
        Assert.Equal(new DateOnly(2022, 7, 14), episode.AirDate);
        Assert.Equal(23, episode.EpisodeFileId);
        Assert.Equal("screenshot", Assert.Single(episode.Images!).CoverType);
    }

    [Fact]
    public async Task GetEpisodeFiles_ReadsRealResponse()
    {
        var files = await CreateClient(StubHttpHandler.Json(Fixtures.Read("sonarr-episodefiles-6.json")))
            .GetEpisodeFilesAsync(6, TestContext.Current.CancellationToken);

        Assert.Equal(3, files.Count);
        Assert.Contains(files, file => file.Id == 23 && file.Path!.EndsWith(".mp4") && file.Size == 13727629);
    }

    [Fact]
    public async Task SetSeasonMonitored_PutsTheWholeSeriesBack_WithOnlyThatSeasonChanged()
    {
        // The series exactly as Sonarr 4 sends it, so every field it has is round tripped
        var series = JsonNode.Parse(Fixtures.Read("sonarr-series.json"))!.AsArray()[0]!.ToJsonString();
        var handler = new StubHttpHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? StubHttpHandler.Response(HttpStatusCode.OK, series, "application/json")
            : new HttpResponseMessage(HttpStatusCode.Accepted)));

        await CreateClient(handler).SetSeriesMonitoredAsync(6, 2, false, TestContext.Current.CancellationToken);

        Assert.Equal(["GET http://sonarr:8989/api/v3/series/6", "PUT http://sonarr:8989/api/v3/series/6"],
            handler.Requests.Select(r => $"{r.Method} {r.RequestUri}"));
        var sent = JsonNode.Parse(handler.Bodies[1]!)!.AsObject();
        var original = JsonNode.Parse(series)!.AsObject();
        Assert.Equal([false, true, false], sent["seasons"]!.AsArray().Select(s => s!["monitored"]!.GetValue<bool>()));
        Assert.True(sent["monitored"]!.GetValue<bool>());
        Assert.Equal(original.Select(p => p.Key).Order(), sent.Select(p => p.Key).Order());
        Assert.Equal(original["statistics"]!.ToJsonString(), sent["statistics"]!.ToJsonString());
    }

    [Fact]
    public async Task SetSeriesMonitored_WithoutASeason_ChangesTheSeries()
    {
        var series = JsonNode.Parse(Fixtures.Read("sonarr-series.json"))!.AsArray()[0]!.ToJsonString();
        var handler = new StubHttpHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? StubHttpHandler.Response(HttpStatusCode.OK, series, "application/json")
            : new HttpResponseMessage(HttpStatusCode.Accepted)));

        await CreateClient(handler).SetSeriesMonitoredAsync(6, null, false, TestContext.Current.CancellationToken);

        var sent = JsonNode.Parse(handler.Bodies[1]!)!;
        Assert.False(sent["monitored"]!.GetValue<bool>());
        Assert.Equal([false, true, true], sent["seasons"]!.AsArray().Select(s => s!["monitored"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task SetSeasonMonitored_ForASeasonSonarrDoesNotHave_IsNotFound()
    {
        var series = JsonNode.Parse(Fixtures.Read("sonarr-series.json"))!.AsArray()[0]!.ToJsonString();
        var handler = StubHttpHandler.Json(series);

        var exception = await Assert.ThrowsAsync<UpstreamException>(() =>
            CreateClient(handler).SetSeriesMonitoredAsync(6, 9, false, TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.NotFound, exception.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ChangeCalls_SendWhatSonarrExpects()
    {
        var handler = StubHttpHandler.Status(HttpStatusCode.Accepted);
        var client = CreateClient(handler);

        await client.DeleteEpisodeFileAsync(23, TestContext.Current.CancellationToken);
        await client.SetEpisodesMonitoredAsync([256, 258], false, TestContext.Current.CancellationToken);
        await client.SearchEpisodesAsync([256], TestContext.Current.CancellationToken);

        Assert.Equal(
        [
            "DELETE http://sonarr:8989/api/v3/episodefile/23 ",
            "PUT http://sonarr:8989/api/v3/episode/monitor {\"episodeIds\":[256,258],\"monitored\":false}",
            "POST http://sonarr:8989/api/v3/command {\"name\":\"EpisodeSearch\",\"episodeIds\":[256]}"
        ], handler.Requests.Select((r, i) => $"{r.Method} {r.RequestUri} {handler.Bodies[i]}"));
    }

    [Fact]
    public async Task DeletingAFileSonarrNoLongerHas_IsNotFound()
    {
        var exception = await Assert.ThrowsAsync<UpstreamException>(() =>
            CreateClient(StubHttpHandler.Status(HttpStatusCode.NotFound)).DeleteEpisodeFileAsync(23, TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.NotFound, exception.Failure);
    }

    private static SonarrClient CreateClient(StubHttpHandler handler)
    {
        var instance = new ArrInstanceOptions { Key = "sonarr", Kind = ArrKind.Sonarr, Url = "http://sonarr:8989", ApiKey = "key" };
        var httpClient = new HttpClient(handler);
        ArrHttpClientConfiguration.Configure(httpClient, instance);
        return new SonarrClient(httpClient, "Sonarr");
    }
}
