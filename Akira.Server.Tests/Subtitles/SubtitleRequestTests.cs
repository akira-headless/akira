using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Akira.Infrastructure.Configuration;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Interfaces.Probing;
using Akira.Contracts.Subtitles;
using Akira.Infrastructure.Probing;
using Akira.Infrastructure.Subtitles;
using Akira.Server.Interfaces.Services;
using Akira.Server.Tests.Playback;
using Akira.Server.Testing;
using Akira.Server.Testing.Playback;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Subtitles;

public abstract class SubtitleRequestTests
{
    protected abstract Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task Options_ListTracksDownloadsAndWhatCanBeTranslated()
    {
        await using var api = await StartAsync();

        var (status, body) = await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-options?language=nl");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("dut", body.GetProperty("language").GetString());
        Assert.Empty(body.GetProperty("existing").EnumerateArray());
        Assert.Equal(["download:fake:best", "download:fake:other"],
            body.GetProperty("downloads").EnumerateArray().Select(d => d.GetProperty("optionId").GetString()));
        var translations = body.GetProperty("translations").EnumerateArray().ToList();
        Assert.Equal(["e1-full", "e1-ger-sub", "e1-signs"], translations.Select(t => t.GetProperty("sourceStreamId").GetString()));
        Assert.All(translations, t => Assert.True(t.GetProperty("translatable").GetBoolean()));
        Assert.Equal("dut", api.Source.Searches.Single().Language);
        Assert.Equal((1, 1), (api.Source.Searches[0].SeasonNumber, api.Source.Searches[0].EpisodeNumber));
    }

    [Fact]
    public async Task Options_SayWhyAnImageSubtitleCannotBeTranslated_AndWhichSourceFailed()
    {
        await using var api = await StartAsync();
        api.Source.Failure = new UpstreamException("Fake", UpstreamFailure.RateLimited, "Fake allows no more searches today.");

        var body = (await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/subtitle-options?language=fr&fileId={api.Ids.HeatFullHd}")).Body;

        Assert.Equal(["Fake allows no more searches today."], body.GetProperty("problems").EnumerateArray().Select(p => p.GetString()));
        var english = body.GetProperty("translations").EnumerateArray().Single(t => t.GetProperty("sourceStreamId").GetString() == "fhd-eng");
        Assert.False(english.GetProperty("translatable").GetBoolean());
        Assert.Contains("image based", english.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Download_IsWrittenNextToTheVideo_AndAnnouncedWithItsTrack()
    {
        await using var api = await StartAsync();

        var (status, queued, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests",
            new { language = "nl", optionId = "download:fake:best" });
        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(("queued", "download", "fake"), (queued.GetProperty("status").GetString(), queued.GetProperty("kind").GetString(),
            queued.GetProperty("provider").GetString()));

        await ProcessAsync(api);

        var done = (await GetAsync(api.Admin, $"/api/v1/subtitle-requests/{queued.GetProperty("id").GetGuid()}")).Body;
        Assert.Equal(("completed", 100), (done.GetProperty("status").GetString(), done.GetProperty("progress").GetInt32()));
        var written = Path.Combine(api.MediaRoot, "tv", "Mayfair Witches", "S01E01.nl.srt");
        Assert.Equal("1\n00:00:01,000 --> 00:00:02,000\nHallo\n", await File.ReadAllTextAsync(written, TestContext.Current.CancellationToken));
        Assert.Equal(["best"], api.Source.Downloads);

        // The new track is in the version like any subtitle next to it, and the finished request says how to load it
        var track = done.GetProperty("track");
        Assert.Equal(done.GetProperty("resultStreamId").GetString(), track.GetProperty("streamId").GetString());
        Assert.Equal(("dut", "subrip"), (track.GetProperty("match").GetProperty("language").GetString(),
            track.GetProperty("match").GetProperty("codec").GetString()));
        Assert.EndsWith($"/subtitles/{track.GetProperty("streamId").GetString()}",
            track.GetProperty("match").GetProperty("externalUrl").GetString());
        var plan = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/playback-plan", new { capabilities = new { } })).Body;
        Assert.Contains(plan.GetProperty("tracks").EnumerateArray(), t => t.GetProperty("streamId").GetString() == track.GetProperty("streamId").GetString());

        // The plan above opened a playback, which is its own event and not part of the request
        var events = (await GetAsync(api.Admin, "/api/v1/events?since=0")).Body.GetProperty("items").EnumerateArray()
            .Where(e => !e.GetProperty("type").GetString()!.StartsWith("playback."))
            .Select(e => (e.GetProperty("type").GetString(), e.GetProperty("payload").TryGetProperty("status", out var s) ? s.GetString() : null)).ToList();
        Assert.Equal([("subtitle.request.updated", "queued"), ("library.item.updated", null), ("subtitle.request.updated", "completed")], events);
    }

    [Fact]
    public async Task WithoutAnOption_ATrackTheFileHas_IsTheAnswer()
    {
        await using var api = await StartAsync();

        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests", new { language = "de" });

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(("existing", "completed", "e1-ger-sub"), (body.GetProperty("kind").GetString(), body.GetProperty("status").GetString(),
            body.GetProperty("resultStreamId").GetString()));
        Assert.Empty(api.Source.Searches);
    }

    [Fact]
    public async Task WithoutAnOption_AndNoTrack_TheBestDownloadIsTaken()
    {
        await using var api = await StartAsync();

        var body = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests", new { language = "nl" })).Body;

        Assert.Equal(("download", "fake"), (body.GetProperty("kind").GetString(), body.GetProperty("provider").GetString()));
    }

    [Fact]
    public async Task Translation_OfAnEmbeddedTrack_ExtractsFollowsAndCleansUp()
    {
        await using var api = await StartAsync();
        var queued = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests",
            new { language = "nl", optionId = "translate:faketranslator:e1-full" })).Body;
        var id = queued.GetProperty("id").GetGuid();

        await ProcessAsync(api);
        var extracted = Path.Combine(api.MediaRoot, "tv", "Mayfair Witches", "S01E01.en.srt");
        Assert.True(File.Exists(extracted));
        Assert.Equal((extracted, "eng", "dut", "srt", MediaKind.Episode), api.Translator.Jobs.Single());
        Assert.Equal("running", (await GetAsync(api.Admin, $"/api/v1/subtitle-requests/{id}")).Body.GetProperty("status").GetString());

        api.Translator.Progress = 40;
        await ProcessAsync(api);
        Assert.Equal(40, (await GetAsync(api.Admin, $"/api/v1/subtitle-requests/{id}")).Body.GetProperty("progress").GetInt32());

        api.Translator.Finish(Path.Combine(api.MediaRoot, "tv", "Mayfair Witches", "S01E01.nl.srt"));
        await ProcessAsync(api);
        var done = (await GetAsync(api.Admin, $"/api/v1/subtitle-requests/{id}")).Body;
        Assert.Equal("completed", done.GetProperty("status").GetString());
        Assert.Equal("dut", done.GetProperty("track").GetProperty("match").GetProperty("language").GetString());
        Assert.False(File.Exists(extracted));
    }

    [Fact]
    public async Task AskingAgain_FollowsTheOwnRequest_AndAnotherUserGetsTheSameSubtitleWithoutASecondDownload()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        using var kid = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "personal", await CreateUserAsync(api.Admin, "Kid")));

        var first = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests",
            new { language = "nl", optionId = "download:fake:best" })).Body.GetProperty("id").GetGuid();
        var again = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests", new { language = "dut" })).Body
            .GetProperty("id").GetGuid();
        var kids = (await PostAsync(kid, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests", new { language = "dut" })).Body
            .GetProperty("id").GetGuid();

        Assert.Equal(first, again);
        Assert.NotEqual(first, kids);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(kid, $"/api/v1/subtitle-requests/{first}")).Status);

        await ProcessAsync(api);

        var done = (await GetAsync(api.Admin, $"/api/v1/subtitle-requests/{first}")).Body;
        var kidsDone = (await GetAsync(kid, $"/api/v1/subtitle-requests/{kids}")).Body;
        Assert.Equal(("completed", done.GetProperty("resultStreamId").GetString()),
            (kidsDone.GetProperty("status").GetString(), kidsDone.GetProperty("resultStreamId").GetString()));
        Assert.Equal(["best"], api.Source.Downloads);
    }

    [Fact]
    public async Task Cancel_StopsTheTranslatorsJob()
    {
        await using var api = await StartAsync();
        var id = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests",
            new { language = "nl", optionId = "translate:faketranslator:e1-full" })).Body.GetProperty("id").GetGuid();
        await ProcessAsync(api);

        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/subtitle-requests/{id}/cancel", new { });

        Assert.Equal((HttpStatusCode.OK, "cancelled"), (status, body.GetProperty("status").GetString()));
        Assert.Equal(["job-1"], api.Translator.Cancelled);
        Assert.False(File.Exists(Path.Combine(api.MediaRoot, "tv", "Mayfair Witches", "S01E01.en.srt")));
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(api.Admin, $"/api/v1/subtitle-requests/{id}/cancel", new { })).Status);
    }

    [Fact]
    public async Task AFailedDownload_SaysWhy()
    {
        await using var api = await StartAsync();
        var id = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests",
            new { language = "nl", optionId = "download:fake:best" })).Body.GetProperty("id").GetGuid();
        api.Source.Failure = new UpstreamException("Fake", UpstreamFailure.RateLimited, "Fake allows no more downloads today.");

        await ProcessAsync(api);

        var body = (await GetAsync(api.Admin, $"/api/v1/subtitle-requests/{id}")).Body;
        Assert.Equal(("failed", "Fake allows no more downloads today."), (body.GetProperty("status").GetString(), body.GetProperty("error").GetString()));

        // An error nobody expected fails the request too: left queued, it would hold up every request behind it
        var odd = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/subtitle-requests",
            new { language = "nl", optionId = "download:fake:best" })).Body.GetProperty("id").GetGuid();
        api.Source.Failure = new FormatException("The input string 'best' was not in a correct format.");

        await ProcessAsync(api);

        Assert.Equal("failed", (await GetAsync(api.Admin, $"/api/v1/subtitle-requests/{odd}")).Body.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("episode", "{\"language\":\"klingonish\"}", HttpStatusCode.BadRequest)]
    [InlineData("series", "{\"language\":\"nl\"}", HttpStatusCode.BadRequest)]
    [InlineData("episode", "{\"language\":\"nl\",\"optionId\":\"made:up\"}", HttpStatusCode.BadRequest)]
    [InlineData("movie", "{\"language\":\"fr\",\"optionId\":\"translate:faketranslator:fhd-eng\"}", HttpStatusCode.BadRequest)]
    [InlineData("alien", "{\"language\":\"nl\"}", HttpStatusCode.Conflict)]
    public async Task UnclearOrImpossibleRequests_AreRefused(string what, string json, HttpStatusCode expected)
    {
        await using var api = await StartAsync();
        var id = what switch
        {
            "episode" => api.Ids.Episode1,
            "series" => api.Ids.Series,
            "movie" => api.Ids.Heat,
            _ => api.Ids.Alien
        };
        var query = what == "movie" ? $"?fileId={api.Ids.HeatFullHd}" : "";

        using var content = new StringContent(json.Replace("}", what == "movie" ? $",\"fileId\":\"{api.Ids.HeatFullHd}\"}}" : "}"),
            System.Text.Encoding.UTF8, "application/json");
        var response = await api.Admin.PostAsync($"/api/v1/items/{id}/subtitle-requests{query}", content, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Providers_SayWhatIsConfigured()
    {
        await using var api = await StartAsync();

        var providers = (await GetAsync(api.Admin, "/api/v1/subtitle-providers")).Body.EnumerateArray()
            .Select(p => (p.GetProperty("key").GetString(), p.GetProperty("kind").GetString(), p.GetProperty("isConfigured").GetBoolean())).ToList();

        Assert.Equal([("fake", "source", true), ("faketranslator", "translator", true)], providers);
    }

    private static async Task ProcessAsync(StartedApi api)
    {
        using var scope = api.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISubtitleRequestProcessor>().ProcessAsync(TestContext.Current.CancellationToken);
    }

    private async Task<StartedApi> StartAsync()
    {
        var (options, database) = await CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var mediaRoot = Directory.CreateTempSubdirectory("akira_subtitles_").FullName;
        var source = new FakeSource();
        var translator = new FakeTranslator();

        var factory = new AkiraApiFactory(options)
        {
            Instances =
            [
                new ArrInstanceOptions { Key = "radarr", Name = "Radarr", Kind = ArrKind.Radarr, Url = "http://radarr", ApiKey = "k" },
                new ArrInstanceOptions { Key = "radarr-4k", Name = "Radarr 4K", Kind = ArrKind.Radarr, Url = "http://radarr4k", ApiKey = "k" },
                new ArrInstanceOptions { Key = "sonarr", Name = "Sonarr", Kind = ArrKind.Sonarr, Url = "http://sonarr", ApiKey = "k" }
            ],
            TestServices = services =>
            {
                services.RemoveAll<ISubtitleSource>();
                services.RemoveAll<ISubtitleTranslator>();
                services.AddSingleton<ISubtitleSource>(source);
                services.AddSingleton<ISubtitleTranslator>(translator);
                services.Replace(ServiceDescriptor.Singleton<ISubtitleExtractor>(new FakeExtractor()));
                services.Replace(ServiceDescriptor.Singleton<IFfprobeRunner>(new SubtitleOnlyFfprobe()));
            }
        };

        PlaybackSeed.Ids ids;
        using (var scope = factory.Services.CreateScope())
        {
            ids = await PlaybackSeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AkiraDbContext>(), mediaRoot,
                TestContext.Current.CancellationToken);
        }

        // The videos exist, subtitles are written next to them
        foreach (var video in new[] { Path.Combine(mediaRoot, "tv", "Mayfair Witches", "S01E01.mkv"), Path.Combine(mediaRoot, "movies", "Heat (1995)", "Heat.mkv") })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(video)!);
            await File.WriteAllBytesAsync(video, [0, 1, 2], TestContext.Current.CancellationToken);
        }

        return new StartedApi(factory, factory.CreateClient(), ids, mediaRoot, source, translator, database);
    }

    protected sealed record StartedApi(
        AkiraApiFactory Factory,
        HttpClient Admin,
        PlaybackSeed.Ids Ids,
        string MediaRoot,
        FakeSource Source,
        FakeTranslator Translator,
        IAsyncDisposable? Database) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            await Factory.DisposeAsync();
            if (Database is not null)
            {
                await Database.DisposeAsync();
            }

            Directory.Delete(MediaRoot, recursive: true);
        }
    }

    protected sealed class FakeSource : ISubtitleSource
    {
        public List<SubtitleSearch> Searches { get; } = [];
        public List<string> Downloads { get; } = [];
        public Exception? Failure { get; set; }

        public string Key => "fake";
        public string Name => "Fake";
        public bool IsConfigured => true;

        public Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleSearch search, CancellationToken cancellationToken)
        {
            Searches.Add(search);
            if (Failure is not null)
            {
                return Task.FromException<IReadOnlyList<SubtitleCandidate>>(Failure);
            }

            return Task.FromResult<IReadOnlyList<SubtitleCandidate>>(
            [
                new SubtitleCandidate("best", search.Language, "S01E01.1080p.WEB", 900, false, false, true, "srt"),
                new SubtitleCandidate("other", search.Language, "S01E01.720p", 20, true, false, false, "srt")
            ]);
        }

        public Task<SubtitleDownload> DownloadAsync(string reference, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException<SubtitleDownload>(Failure);
            }

            Downloads.Add(reference);
            return Task.FromResult(new SubtitleDownload("1\n00:00:01,000 --> 00:00:02,000\nHallo\n"u8.ToArray(), "srt"));
        }
    }

    protected sealed class FakeTranslator : ISubtitleTranslator
    {
        private string? _result;

        public List<(string, string, string, string, MediaKind)> Jobs { get; } = [];
        public List<string> Cancelled { get; } = [];
        public int Progress { get; set; }

        public string Key => "faketranslator";
        public string Name => "Fake translator";
        public bool IsConfigured => true;
        public IReadOnlySet<string> Formats { get; } = new HashSet<string> { "srt", "ass" };

        public void Finish(string path)
        {
            File.WriteAllText(path, "1\n00:00:01,000 --> 00:00:02,000\nVertaald\n");
            _result = path;
        }

        public Task<string> StartAsync(TranslationJob job, CancellationToken cancellationToken)
        {
            Jobs.Add((job.SubtitlePath, job.SourceLanguage, job.TargetLanguage, job.Format, job.Type));
            return Task.FromResult("job-" + Jobs.Count);
        }

        public Task<TranslationProgress> GetAsync(string jobId, CancellationToken cancellationToken) =>
            Task.FromResult(_result is null
                ? new TranslationProgress(TranslationState.Running, Progress, null, null)
                : new TranslationProgress(TranslationState.Completed, 100, _result, null));

        public Task CancelAsync(string jobId, CancellationToken cancellationToken)
        {
            Cancelled.Add(jobId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExtractor : ISubtitleExtractor
    {
        public Task ExtractAsync(string videoPath, int streamIndex, string targetPath, CancellationToken cancellationToken) =>
            File.WriteAllTextAsync(targetPath, "1\n00:00:01,000 --> 00:00:02,000\nHello\n", cancellationToken);

        public Task ExtractAsync(string videoPath, IReadOnlyList<SubtitleExtraction> tracks, CancellationToken cancellationToken) =>
            Task.WhenAll(tracks.Select(track => ExtractAsync(videoPath, track.StreamIndex, track.TargetPath, cancellationToken)));
    }

    // Reads every subtitle file back as one SubRip track, which is what the rescan needs of ffprobe
    private sealed class SubtitleOnlyFfprobe : IFfprobeRunner
    {
        public Task<string> GetVersionAsync(CancellationToken cancellationToken) => Task.FromResult("ffprobe test");

        public Task<FfprobeOutput> ProbeAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<FfprobeOutput> ProbeSubtitleAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(new FfprobeOutput(
                [new FfprobeStream(0, "subtitle", "subrip", null, null, null, null, null, null, null, null, null, null, null, "120", null, null, null)],
                null, null));
    }
}
