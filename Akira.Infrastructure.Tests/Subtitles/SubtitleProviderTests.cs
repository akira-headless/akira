using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Akira.Core.Enum;
using Akira.Core.Media;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Lingarr;
using Akira.Infrastructure.Integrations.OpenSubtitles;
using Akira.Contracts.Subtitles;
using Akira.Infrastructure.Subtitles;
using Akira.Infrastructure.Sync;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Subtitles;

// The OpenSubtitles answers follow their REST API documentation (api.opensubtitles.com/api/v1), there is no
// account to record real ones with yet. Lingarr's are the shapes of its own controllers.
public class SubtitleProviderTests
{
    private const string Search = """
        {"total_count": 3, "data": [
          {"id": "1", "attributes": {"language": "nl", "download_count": 900, "hearing_impaired": false, "foreign_parts_only": false,
            "moviehash_match": false, "release": "Heat.1995.1080p.BluRay", "files": [{"file_id": 111, "file_name": "Heat.1995.1080p.srt"}]}},
          {"id": "2", "attributes": {"language": "nl", "download_count": 50, "hearing_impaired": true, "foreign_parts_only": false,
            "moviehash_match": true, "release": "Heat.1995.2160p.Remux", "files": [{"file_id": 222, "file_name": "Heat.2160p.srt"}]}},
          {"id": "3", "attributes": {"language": "nl", "download_count": 5000, "files": []}}
        ]}
        """;

    [Fact]
    public async Task OpenSubtitles_SearchesAMovieByItsIds_AndPutsAFileHashMatchFirst()
    {
        var handler = StubHttpHandler.Json(Search);
        var source = OpenSubtitles(handler);

        var found = await source.SearchAsync(new SubtitleSearch("dut", MediaKind.Movie, 949, "tt0113277", null, null, null, null,
            "8e245d9679d31e12"), TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        // In alphabetical order, which OpenSubtitles asks for and otherwise answers with a redirect
        Assert.Equal("https://api.opensubtitles.com/api/v1/subtitles?imdb_id=113277&languages=nl&moviehash=8e245d9679d31e12&tmdb_id=949",
            request.RequestUri!.ToString());
        Assert.Equal("osk", request.Headers.GetValues("Api-Key").Single());
        Assert.StartsWith("Akira", request.Headers.UserAgent.ToString());
        Assert.Equal(["222", "111"], found.Select(candidate => candidate.Reference));
        Assert.Equal(("dut", true, true, "Heat.1995.2160p.Remux"), (found[0].Language, found[0].HashMatch, found[0].HearingImpaired, found[0].Release));
    }

    [Fact]
    public async Task OpenSubtitles_SearchesAnEpisodeThroughItsSeries()
    {
        var handler = StubHttpHandler.Json("""{"data": []}""");

        await OpenSubtitles(handler).SearchAsync(new SubtitleSearch("por", MediaKind.Episode, null, null, 1399, "tt0944947", 2, 9, null),
            TestContext.Current.CancellationToken);

        Assert.Equal("https://api.opensubtitles.com/api/v1/subtitles?episode_number=9&languages=pt-pt%2Cpt-br&parent_imdb_id=944947&parent_tmdb_id=1399&season_number=2",
            handler.Requests[0].RequestUri!.ToString().Replace("languages=pt-pt,pt-br", "languages=pt-pt%2Cpt-br"));
    }

    [Fact]
    public async Task OpenSubtitles_DownloadLogsInOnce_ThenFetchesTheLink()
    {
        var handler = new StubHttpHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/login" => StubHttpHandler.Response(HttpStatusCode.OK, """{"token": "user-token", "status": 200}""", "application/json"),
            "/api/v1/download" => StubHttpHandler.Response(HttpStatusCode.OK,
                """{"link": "https://dl.opensubtitles.com/file/222", "remaining": 19}""", "application/json"),
            _ => StubHttpHandler.Response(HttpStatusCode.OK, "1\n00:00:01,000 --> 00:00:02,000\nHallo\n", "application/x-subrip")
        }));
        var source = OpenSubtitles(handler, username: "rowan");

        var first = await source.DownloadAsync("222", TestContext.Current.CancellationToken);
        await source.DownloadAsync("222", TestContext.Current.CancellationToken);

        Assert.Contains("Hallo", System.Text.Encoding.UTF8.GetString(first.Content));
        Assert.Equal(["/api/v1/login", "/api/v1/download", "/file/222", "/api/v1/download", "/file/222"],
            handler.Requests.Select(request => request.RequestUri!.AbsolutePath));
        Assert.Equal("Bearer user-token", handler.Requests[1].Headers.Authorization!.ToString());
        Assert.Equal(222, JsonDocument.Parse(handler.Bodies[1]!).RootElement.GetProperty("file_id").GetInt64());
        // The file itself is fetched without the household's key or token
        Assert.Equal((false, null), (handler.Requests[2].Headers.Contains("Api-Key"), handler.Requests[2].Headers.Authorization));
    }

    [Fact]
    public async Task OpenSubtitles_ALinkThatIsNotTheirs_IsRefused_AndARejectedTokenIsRenewedOnce()
    {
        var logins = 0;
        var downloads = 0;
        var handler = new StubHttpHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/login" => StubHttpHandler.Response(HttpStatusCode.OK, $$"""{"token": "token-{{++logins}}", "status": 200}""", "application/json"),
            "/api/v1/download" when ++downloads == 1 => StubHttpHandler.Response(HttpStatusCode.Unauthorized, "{}", "application/json"),
            _ => StubHttpHandler.Response(HttpStatusCode.OK, """{"link": "http://example.com/file/222"}""", "application/json")
        }));

        var exception = await Assert.ThrowsAsync<UpstreamException>(() =>
            OpenSubtitles(handler, username: "rowan").DownloadAsync("222", TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.Malformed, exception.Failure);
        Assert.Equal(["/api/v1/login", "/api/v1/download", "/api/v1/login", "/api/v1/download"], handler.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.Equal("Bearer token-2", handler.Requests[3].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task OpenSubtitles_ADaysQuotaUsedUp_IsRateLimited()
    {
        var exception = await Assert.ThrowsAsync<UpstreamException>(() =>
            OpenSubtitles(StubHttpHandler.Status(HttpStatusCode.NotAcceptable)).DownloadAsync("222", TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.RateLimited, exception.Failure);
        Assert.Contains("Username and Password", exception.Message);
    }

    [Fact]
    public async Task OpenSubtitles_WithoutAKey_IsNotConfigured()
    {
        var source = OpenSubtitles(StubHttpHandler.Json("{}"), apiKey: null);

        Assert.False(source.IsConfigured);
        var exception = await Assert.ThrowsAsync<UpstreamException>(() => source.DownloadAsync("1", TestContext.Current.CancellationToken));
        Assert.Equal(UpstreamFailure.NotConfigured, exception.Failure);
    }

    [Fact]
    public async Task Lingarr_GetsThePathItSees_TwoLetterCodes_AndTheFormatWithItsDot()
    {
        var handler = StubHttpHandler.Json("""{"jobId": 42}""");

        var job = await Lingarr(handler).StartAsync(new TranslationJob("/data/media/movies/Heat/Heat.en.srt", "eng", "dut", "srt", MediaKind.Movie),
            TestContext.Current.CancellationToken);

        Assert.Equal("42", job);
        Assert.Equal("http://lingarr:9876/api/translate/file", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("lak", handler.Requests[0].Headers.GetValues("X-Api-Key").Single());
        var body = JsonDocument.Parse(handler.Bodies[0]!).RootElement;
        Assert.Equal(("/movies/Heat/Heat.en.srt", "en", "nl", "Movie", ".srt"), (body.GetProperty("subtitlePath").GetString(),
            body.GetProperty("sourceLanguage").GetString(), body.GetProperty("targetLanguage").GetString(), body.GetProperty("mediaType").GetString(),
            body.GetProperty("subtitleFormat").GetString()));
    }

    [Fact]
    public async Task Lingarr_WithoutAKey_SendsNoKeyHeader()
    {
        var handler = StubHttpHandler.Json("""{"jobId": 7}""");
        var lingarr = Lingarr(handler, apiKey: null);

        await lingarr.StartAsync(new TranslationJob("/data/media/movies/Heat/Heat.en.srt", "eng", "dut", "srt", MediaKind.Movie),
            TestContext.Current.CancellationToken);

        Assert.True(lingarr.IsConfigured);
        Assert.False(handler.Requests[0].Headers.Contains("X-Api-Key"));
    }

    [Fact]
    public async Task Lingarr_AskingForAKeyWeDoNotHave_SaysWhichSettingToSet()
    {
        var exception = await Assert.ThrowsAsync<UpstreamException>(() =>
            Lingarr(StubHttpHandler.Status(HttpStatusCode.Unauthorized), apiKey: null).GetAsync("7", TestContext.Current.CancellationToken));

        Assert.Equal(UpstreamFailure.Unauthorized, exception.Failure);
        Assert.Equal("Lingarr has authentication on, set Subtitles:Lingarr:ApiKey.", exception.Message);
    }

    [Theory]
    [InlineData("InProgress", 40, null, TranslationState.Running, null)]
    [InlineData("Completed", 100, "/movies/Heat/Heat.nl.srt", TranslationState.Completed, "/data/media/movies/Heat/Heat.nl.srt")]
    [InlineData("Failed", 10, null, TranslationState.Failed, null)]
    [InlineData("Pending", 0, null, TranslationState.Queued, null)]
    public async Task Lingarr_ProgressAndTheResultMapBackToThisServer(string status, int progress, string? lingarrPath,
        TranslationState expected, string? serverPath)
    {
        var handler = StubHttpHandler.Json(JsonSerializer.Serialize(new { id = 42, status, progress, translatedSubtitle = lingarrPath,
            errorMessage = status == "Failed" ? "No translation service" : null }));

        var result = await Lingarr(handler).GetAsync("42", TestContext.Current.CancellationToken);

        Assert.Equal("http://lingarr:9876/api/translationrequest/42", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal((expected, progress, serverPath), (result.Status, result.Progress, result.ResultPath));
        Assert.Equal(status == "Failed" ? "No translation service" : null, result.Error);
    }

    [Fact]
    public async Task FileHash_IsTheSizePlusTheWordsOfBothEnds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"akira_hash_{Guid.NewGuid():N}.bin");
        var bytes = new byte[200 * 1024];
        bytes[0] = 1;
        bytes[^8] = 2;
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        try
        {
            // 204800 bytes, plus 1 from the first word and 2 from the last
            Assert.Equal((204800UL + 1 + 2).ToString("x16"), await FileHash.ComputeAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FreePath_NamesTheFileTheWayTheScannerReadsIt_AndNumbersWhenTaken()
    {
        var directory = Directory.CreateTempSubdirectory("akira_place_").FullName;
        try
        {
            var video = Path.Combine(directory, "Heat (1995).mkv");
            Assert.Equal(Path.Combine(directory, "Heat (1995).nl.srt"), SubtitlePlacement.FreePath(video, "dut", "srt"));
            File.WriteAllText(Path.Combine(directory, "Heat (1995).nl.srt"), "");
            Assert.Equal(Path.Combine(directory, "Heat (1995).nl.2.srt"), SubtitlePlacement.FreePath(video, "dut", ".srt"));
            Assert.Equal(Path.Combine(directory, "Heat (1995).fil.forced.ass"), SubtitlePlacement.FreePath(video, "fil", "ass", forced: true));

            var parsed = SubtitleFiles.Parse("Heat (1995).mkv", "Heat (1995).nl.2.srt");
            Assert.Equal("dut", parsed!.Language);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ToTwoLetter_IsTheWayBack()
    {
        Assert.Equal(["nl", "de", "en", "pt"], new[] { "dut", "ger", "eng", "por" }.Select(Languages.ToTwoLetter));
        Assert.Null(Languages.ToTwoLetter("fil"));
    }

    private static OpenSubtitlesClient OpenSubtitles(StubHttpHandler handler, string? apiKey = "osk", string? username = null) =>
        new(new SingleClientFactory(handler), Monitor(new SubtitleOptions
        {
            OpenSubtitles = new OpenSubtitlesOptions { ApiKey = apiKey, Username = username, Password = username is null ? null : "secret" }
        }), TimeProvider.System);

    private static LingarrClient Lingarr(StubHttpHandler handler, string? apiKey = "lak") =>
        new(new SingleClientFactory(handler), Monitor(new SubtitleOptions
        {
            Lingarr = new LingarrOptions
            {
                Url = "http://lingarr:9876", ApiKey = apiKey,
                PathMappings = [new PathMappingOptions { From = "/data/media", To = "/" }]
            }
        }));

    private static IOptionsMonitor<SubtitleOptions> Monitor(SubtitleOptions options) => new FixedOptions(options);

    private sealed class FixedOptions(SubtitleOptions options) : IOptionsMonitor<SubtitleOptions>
    {
        public SubtitleOptions CurrentValue => options;

        public SubtitleOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<SubtitleOptions, string?> listener) => null;
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
