using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Core.Playback;
using Akira.Infrastructure.Trickplay;
using Akira.Infrastructure.Markers;
using Akira.Server.Events;
using Akira.Server.Services.Playback;
using Akira.Infrastructure.Configuration;
using Akira.Infrastructure.Data;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Interfaces.Probing;
using Akira.Infrastructure.Probing;
using Akira.Server.Interfaces.Services;
using Akira.Server.Testing;
using Akira.Server.Testing.Playback;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Playback;

public abstract class PlaybackFlowTests
{
    private static readonly object UhdTv = new
    {
        capabilities = new
        {
            videoCodecs = new[] { "h264", "hevc" },
            hdr = new[] { "hdr10" },
            audioPassthrough = new[] { "ac3", "eac3", "truehd", "dtshd" },
            audioDecode = new[] { "aac", "ac3", "flac" },
            subtitleFormats = new[] { "srt", "ass", "pgs" },
            accessModes = new[] { "filePath", "http" }
        }
    };

    private static readonly object SdrTablet = new
    {
        capabilities = new
        {
            videoCodecs = new[] { "h264", "hevc" },
            hdr = Array.Empty<string>(),
            audioDecode = new[] { "aac", "ac3" },
            subtitleFormats = new[] { "srt", "ass" },
            accessModes = new[] { "http" }
        }
    };

    // A browser through hls.js: no Matroska, no HDR, no AC-3, nothing but HLS
    private static readonly object Browser = new
    {
        capabilities = new
        {
            videoCodecs = new[] { "h264" },
            hdr = Array.Empty<string>(),
            audioDecode = new[] { "aac", "mp3" },
            subtitleFormats = new[] { "vtt" },
            containers = new[] { "mp4" },
            accessModes = new[] { "hls" }
        }
    };

    // What Chrome and Brave on Windows sent: HEVC and Matroska direct, no AC-3
    private static readonly object Chromium = new
    {
        capabilities = new
        {
            videoCodecs = new[] { "h264", "hevc", "av1", "vp9" },
            hdr = Array.Empty<string>(),
            audioDecode = new[] { "aac", "mp3", "opus", "vorbis", "flac" },
            subtitleFormats = new[] { "vtt", "srt", "ass" },
            containers = new[] { "mp4", "webm", "mkv" },
            accessModes = new[] { "http", "hls" }
        }
    };

    protected abstract Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task Plan_ForAUhdTv_PicksThe4kVersionTrueHdAndTheForcedTrack()
    {
        await using var api = await StartAsync();

        var (status, plan, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", UhdTv);

        Assert.Equal(HttpStatusCode.OK, status);
        var version = plan.GetProperty("version");
        Assert.Equal((api.Ids.HeatUhd, "radarr-4k", "Radarr 4K", "best-match", true),
            (version.GetProperty("fileId").GetGuid(), version.GetProperty("instance").GetString(), version.GetProperty("instanceName").GetString(),
                version.GetProperty("reason").GetString(), version.GetProperty("isCompatible").GetBoolean()));

        var sources = plan.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(["filePath", "http"], sources.Select(s => s.GetProperty("mode").GetString()));
        Assert.Equal("nfs://nas/media/movies-4k/Heat (1995)/Heat.mkv", sources[0].GetProperty("path").GetString());
        Assert.Equal($"/api/v1/files/{api.Ids.HeatUhd}/stream", sources[1].GetProperty("url").GetString());

        Assert.Equal(("uhd-truehd", "default-track"), Pick(plan, "audio"));
        Assert.Equal(("uhd-forced", "forced"), Pick(plan, "subtitle"));
        var audioMatch = plan.GetProperty("audio").GetProperty("match");
        Assert.Equal(("eng", "truehd", 8, 1), (audioMatch.GetProperty("language").GetString(), audioMatch.GetProperty("codec").GetString(),
            audioMatch.GetProperty("channels").GetInt32(), audioMatch.GetProperty("indexHint").GetInt32()));

        // Every audio and subtitle track is listed, so a player can map its own track list to stable ids
        var tracks = plan.GetProperty("tracks").EnumerateArray().ToList();
        Assert.Equal(5, tracks.Count);
        var dutch = tracks.Single(t => t.GetProperty("streamId").GetString() == "uhd-dut").GetProperty("match");
        Assert.Equal($"/api/v1/files/{api.Ids.HeatUhd}/subtitles/uhd-dut", dutch.GetProperty("externalUrl").GetString());
        Assert.Equal("nfs://nas/media/movies-4k/Heat (1995)/Heat.nl.srt", dutch.GetProperty("externalPath").GetString());

        Assert.Equal("Opening", plan.GetProperty("chapters")[0].GetProperty("title").GetString());
        Assert.Equal(0, plan.GetProperty("resumePositionMs").GetInt64());
    }

    [Fact]
    public async Task Plan_ForAnSdrTablet_PicksTheSdrVersionOverHttpOnly()
    {
        await using var api = await StartAsync();

        var (_, plan, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", SdrTablet);

        Assert.Equal(api.Ids.HeatFullHd, plan.GetProperty("version").GetProperty("fileId").GetGuid());
        Assert.Equal(["http"], plan.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("mode").GetString()));
        Assert.Equal("fhd-ac3", Pick(plan, "audio").StreamId);

        // The forced track is PGS, which this client cannot show
        Assert.Equal((null, "forced-subtitle-unsupported"), Pick(plan, "subtitle"));
    }

    [Fact]
    public async Task Plan_RequestedVersion_IsPlayedEvenIfAnotherFitsBetter()
    {
        await using var api = await StartAsync();

        var (_, plan, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan",
            new { capabilities = new { hdr = new[] { "hdr10" } }, fileId = api.Ids.HeatFullHd });

        Assert.Equal(("requested", api.Ids.HeatFullHd),
            (plan.GetProperty("version").GetProperty("reason").GetString(), plan.GetProperty("version").GetProperty("fileId").GetGuid()));
    }

    [Theory]
    [InlineData("series", HttpStatusCode.BadRequest)]
    [InlineData("alien", HttpStatusCode.Conflict)]
    [InlineData("unknown", HttpStatusCode.NotFound)]
    public async Task Plan_ForSomethingUnplayable_SaysWhy(string what, HttpStatusCode expected)
    {
        await using var api = await StartAsync();
        var id = what switch { "series" => api.Ids.Series, "alien" => api.Ids.Alien, _ => Guid.NewGuid() };

        Assert.Equal(expected, (await PostAsync(api.Admin, $"/api/v1/items/{id}/playback-plan", UhdTv)).Status);
    }

    [Fact]
    public async Task Plan_WithoutAUsableSource_IsAConflict()
    {
        await using var api = await StartAsync(clientMappings: false);

        var (status, body, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan",
            new { capabilities = new { accessModes = new[] { "filePath" } } });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Playback:ClientPathMappings", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task WatchStateImport_AddsWhatAnotherServerKnew_AndNeverOverwritesNewerStateHere()
    {
        await using var api = await StartAsync();
        object[] entries =
        [
            new { kind = "movie", tmdbId = 949, played = true, playCount = 3, lastPlayedAt = "2025-01-02T20:00:00Z" },
            new { kind = "episode", tvdbId = 1, seasonNumber = 1, episodeNumber = 2, played = false, positionMs = 1_200_000, lastPlayedAt = "2025-01-03T20:00:00Z" },
            // Not in the library yet: kept for when it arrives
            new { kind = "movie", tmdbId = 603, played = true },
            new { kind = "episode", seasonNumber = 1, episodeNumber = 1, played = true }
        ];

        var (_, dry, _) = await PostAsync(api.Admin, "/api/v1/watch-state/import", new { entries, dryRun = true });
        Assert.Equal((3, 0, 3), (dry.GetProperty("updated").GetInt32(), dry.GetProperty("unchanged").GetInt32(),
            dry.GetProperty("unmatched")[0].GetInt32()));
        Assert.Equal(JsonValueKind.Null, (await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}")).Body.GetProperty("userState").ValueKind);

        var (status, done, _) = await PostAsync(api.Admin, "/api/v1/watch-state/import", new { entries });
        Assert.Equal((HttpStatusCode.OK, 3), (status, done.GetProperty("updated").GetInt32()));
        var heat = (await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}")).Body.GetProperty("userState");
        Assert.Equal((true, 3), (heat.GetProperty("isPlayed").GetBoolean(), heat.GetProperty("playCount").GetInt32()));
        var episode = (await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode2}")).Body.GetProperty("userState");
        Assert.Equal((false, 1_200_000), (episode.GetProperty("isPlayed").GetBoolean(), episode.GetProperty("positionMs").GetInt64()));

        // An older position from the other server loses to the one here
        object[] older = [new { kind = "episode", tvdbId = 1, seasonNumber = 1, episodeNumber = 2, played = false, positionMs = 600_000, lastPlayedAt = "2024-06-01T20:00:00Z" }];
        var (_, again, _) = await PostAsync(api.Admin, "/api/v1/watch-state/import", new { entries = older });
        Assert.Equal((0, 1), (again.GetProperty("updated").GetInt32(), again.GetProperty("unchanged").GetInt32()));
        Assert.Equal(1_200_000, (await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode2}")).Body.GetProperty("userState")
            .GetProperty("positionMs").GetInt64());
    }

    [Fact]
    public async Task ProgressAndStop_ResumeThenMarkPlayed()
    {
        await using var api = await StartAsync();
        var session = await PlanAsync(api, api.Ids.Heat);

        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/progress",
            new { positionMs = 1_200_000, isPaused = false }));

        var state = (await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}")).Body.GetProperty("userState");
        Assert.Equal((1_200_000, false), (state.GetProperty("positionMs").GetInt64(), state.GetProperty("isPlayed").GetBoolean()));
        Assert.Equal(["Heat"], Titles((await GetAsync(api.Admin, "/api/v1/library/continue-watching")).Body));
        Assert.Equal(1_200_000, (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", UhdTv)).Body
            .GetProperty("resumePositionMs").GetInt64());

        var second = await PlayAsync(api, api.Ids.Heat);
        var (stopped, stopBody, _) = await PostAsync(api.Admin, $"/api/v1/sessions/{second}/stop", new { positionMs = 5_500_000 });
        Assert.Equal(HttpStatusCode.OK, stopped);
        Assert.Equal((true, 1, 0), (stopBody.GetProperty("isPlayed").GetBoolean(), stopBody.GetProperty("playCount").GetInt32(),
            stopBody.GetProperty("positionMs").GetInt64()));

        Assert.Empty(Titles((await GetAsync(api.Admin, "/api/v1/library/continue-watching")).Body));
        Assert.Equal(HttpStatusCode.NotFound, await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{second}/progress",
            new { positionMs = 1, isPaused = false }));
    }

    [Fact]
    public async Task StoppingInTheFirstMinute_KeepsNoResumePoint()
    {
        await using var api = await StartAsync();
        var session = await PlayAsync(api, api.Ids.Heat);

        var (_, body, _) = await PostAsync(api.Admin, $"/api/v1/sessions/{session}/stop", new { positionMs = 40_000 });

        Assert.Equal((false, 0), (body.GetProperty("isPlayed").GetBoolean(), body.GetProperty("positionMs").GetInt64()));
    }

    [Fact]
    public async Task TrackChange_IsRememberedForTheNextEpisode()
    {
        await using var api = await StartAsync();
        var first = await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/playback-plan", UhdTv);
        Assert.Equal(("e1-eng", "default-track"), Pick(first.Body, "audio"));
        Assert.Equal(("e1-signs", "forced-heuristic"), Pick(first.Body, "subtitle"));

        var session = first.Body.GetProperty("sessionId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/tracks",
            new { audioStreamId = "e1-ger", subtitleStreamId = "e1-full" }));

        var next = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode2}/playback-plan", UhdTv)).Body;
        Assert.Equal(("e2-ger", "remembered-series"), Pick(next, "audio"));
        Assert.Equal(("e2-full", "remembered-series"), Pick(next, "subtitle"));

        // A movie is its own scope, the series choice does not leak into it
        Assert.Equal("default-track", Pick((await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", UhdTv)).Body, "audio").Reason);
    }

    [Fact]
    public async Task SubtitlesTurnedOff_AreRememberedOff()
    {
        await using var api = await StartAsync();
        var session = await PlanAsync(api, api.Ids.Episode1);

        await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/tracks", new { subtitlesOff = true });

        var next = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode2}/playback-plan", UhdTv)).Body;
        Assert.Equal((null, "remembered-series-off"), Pick(next, "subtitle"));
    }

    [Theory]
    [InlineData("{\"audioStreamId\":\"fhd-ac3\"}")]
    [InlineData("{\"audioStreamId\":\"e1-full\"}")]
    [InlineData("{\"subtitleStreamId\":\"e1-full\",\"subtitlesOff\":true}")]
    public async Task TrackChange_WithATrackOfAnotherFileOrKind_IsRejected(string body)
    {
        await using var api = await StartAsync();
        var session = await PlanAsync(api, api.Ids.Episode1);

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await api.Admin.PostAsync($"/api/v1/sessions/{session}/tracks", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SeriesRules_OverrideGlobal_AndResetForgetsThem()
    {
        await using var api = await StartAsync();
        await PostLikePutAsync(api.Admin, "/api/v1/users/me/track-preferences", new { audioLanguages = new[] { "eng" } });

        var (set, prefs, _) = await PostLikePutAsync(api.Admin, $"/api/v1/items/{api.Ids.Series}/track-preferences",
            new { audioLanguages = new[] { "de" } });
        Assert.Equal(HttpStatusCode.OK, set);
        Assert.Equal(("series", "ger"), (prefs.GetProperty("scope").GetString(),
            prefs.GetProperty("effective").GetProperty("audioLanguages")[0].GetString()));

        Assert.Equal(("e1-ger", "preferred-language"),
            Pick((await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/playback-plan", UhdTv)).Body, "audio"));

        var overview = (await GetAsync(api.Admin, "/api/v1/users/me/track-preferences")).Body;
        Assert.Equal("Mayfair Witches", overview.GetProperty("overrides")[0].GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(api.Admin, HttpMethod.Delete, $"/api/v1/items/{api.Ids.Series}/track-preferences"));
        Assert.Equal("e1-eng", Pick((await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/playback-plan", UhdTv)).Body, "audio").StreamId);
    }

    [Fact]
    public async Task HouseholdLanguages_FromTheServer_ApplyUntilAUserSetsTheirOwn()
    {
        await using var api = await StartAsync(extra: new Dictionary<string, string>
        {
            ["Playback:Languages:Subtitles"] = "nl, en",
            ["Playback:Languages:SubtitleMode"] = "Always"
        });

        // Nobody set anything, the household's Dutch applies
        Assert.Equal(("uhd-dut", "always"), Pick((await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", UhdTv)).Body, "subtitle"));
        var overview = (await GetAsync(api.Admin, "/api/v1/users/me/track-preferences")).Body;
        Assert.Equal(["dut", "eng"], overview.GetProperty("serverDefault").GetProperty("subtitleLanguages").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("global").GetProperty("subtitleLanguages").ValueKind);
        Assert.Equal(("always", "dut"), (overview.GetProperty("effective").GetProperty("subtitleMode").GetString(),
            overview.GetProperty("effective").GetProperty("subtitleLanguages")[0].GetString()));

        // The user's own choice wins, field by field
        await PostLikePutAsync(api.Admin, "/api/v1/users/me/track-preferences", new { subtitleLanguages = new[] { "en" } });
        Assert.Equal(("uhd-eng", "always"), Pick((await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", UhdTv)).Body, "subtitle"));
    }

    [Fact]
    public async Task Rules_WithAnUnknownLanguage_AreRejected()
    {
        await using var api = await StartAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostLikePutAsync(api.Admin, "/api/v1/users/me/track-preferences", new { audioLanguages = new[] { "klingonish" } })).Status);
    }

    [Fact]
    public async Task PauseStopAndTrackChanges_AreEventsForThatUserOnly()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        using var kidPhone = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "personal", await CreateUserAsync(api.Admin, "Kid")));
        var session = await PlanAsync(api, api.Ids.Episode1);
        var sockets = api.Factory.Server.CreateWebSocketClient();
        sockets.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {TestAdmin.ApiKey}";
        using var socket = await sockets.ConnectAsync(new Uri(api.Factory.Server.BaseAddress, "api/v1/events/ws"), TestContext.Current.CancellationToken);
        Assert.Equal(EventTypes.StreamReady, (await ReceiveAsync(socket)).GetProperty("type").GetString());

        // Playing on is no stored event, pausing is. The position goes live to the user's sockets, at most every 5 seconds
        await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/progress", new { positionMs = 10_000, isPaused = false });
        await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/progress", new { positionMs = 20_000, isPaused = false });
        await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/progress", new { positionMs = 90_000, isPaused = true });
        var live = new[] { await ReceiveAsync(socket), await ReceiveAsync(socket), await ReceiveAsync(socket) };
        Assert.Equal([EventTypes.PlaybackStarted, EventTypes.PlaybackProgress, EventTypes.WatchStateChanged], live.Select(m => m.GetProperty("type").GetString()));
        Assert.Equal((0L, session, 10_000L), (live[1].GetProperty("sequence").GetInt64(), live[1].GetProperty("payload").GetProperty("sessionId").GetGuid(),
            live[1].GetProperty("payload").GetProperty("positionMs").GetInt64()));
        await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/tracks", new { audioStreamId = "e1-ger" });
        await PostAsync(api.Admin, $"/api/v1/sessions/{session}/stop", new { positionMs = PlaybackSeed.EpisodeDurationMs });
        await PostLikePutAsync(api.Admin, "/api/v1/users/me/track-preferences", new { subtitleMode = "off" });

        var events = (await GetAsync(api.Admin, "/api/v1/events?since=0")).Body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["playback.started", "watchstate.changed", "trackpreference.changed", "watchstate.changed", "playback.stopped", "trackpreference.changed"],
            events.Select(e => e.GetProperty("type").GetString()));

        Assert.Equal((session, false), (events[0].GetProperty("payload").GetProperty("sessionId").GetGuid(),
            events[0].GetProperty("payload").GetProperty("transcoding").GetBoolean()));
        var paused = events[1].GetProperty("payload");
        Assert.Equal((api.Ids.Episode1, "series:tvdb:1:s1:e1", 90_000L, false),
            (paused.GetProperty("itemId").GetGuid(), paused.GetProperty("identityKey").GetString(), paused.GetProperty("positionMs").GetInt64(),
                paused.GetProperty("isPlayed").GetBoolean()));
        Assert.Equal(("series", "series:tvdb:1"),
            (events[2].GetProperty("payload").GetProperty("scope").GetString(), events[2].GetProperty("payload").GetProperty("identityKey").GetString()));
        Assert.True(events[3].GetProperty("payload").GetProperty("isPlayed").GetBoolean());
        Assert.True(events[4].GetProperty("payload").GetProperty("played").GetBoolean());
        Assert.Equal(("global", JsonValueKind.Null),
            (events[5].GetProperty("payload").GetProperty("scope").GetString(), events[5].GetProperty("payload").GetProperty("identityKey").ValueKind));

        Assert.Equal(0, (await GetAsync(kidPhone, "/api/v1/events?since=0")).Body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ProbedItems_AreAnnouncedAsUpdated()
    {
        await using var api = await StartAsync();
        using (var scope = api.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILibraryEventService>().PublishProbeAsync(
                new ProbeRunResult(1, 1, 0, [api.Ids.Episode2]), null, TestContext.Current.CancellationToken);
        }

        var events = (await GetAsync(api.Admin, "/api/v1/events?since=0")).Body.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(["library.item.updated", "scan.progress"], events.Select(e => e.GetProperty("type").GetString()));
        var item = events[0].GetProperty("payload");
        Assert.Equal(("episode", api.Ids.Series), (item.GetProperty("type").GetString(), item.GetProperty("seriesId").GetGuid()));
        Assert.Equal(("probe", 1, 1), (events[1].GetProperty("payload").GetProperty("kind").GetString(),
            events[1].GetProperty("payload").GetProperty("probed").GetInt32(), events[1].GetProperty("payload").GetProperty("missing").GetInt32()));
    }

    [Fact]
    public async Task AnotherUsersSession_LooksLikeNoSession()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(api.Admin, "Kid");
        using var kidPhone = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "personal", kid));
        var session = await PlanAsync(api, api.Ids.Heat);

        Assert.Equal(HttpStatusCode.NotFound, await SendAsync(kidPhone, HttpMethod.Post, $"/api/v1/sessions/{session}/progress",
            new { positionMs = 1, isPaused = false }));

        // And watch state is per user
        await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/progress", new { positionMs = 900_000, isPaused = false });
        Assert.Equal(JsonValueKind.Null, (await GetAsync(kidPhone, $"/api/v1/items/{api.Ids.Heat}")).Body.GetProperty("userState").ValueKind);
    }

    [Fact]
    public async Task NextUp_FollowsWhatWasWatched_AndUnplayedCountsDown()
    {
        await using var api = await StartAsync();
        Assert.Equal(3, (await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Series}")).Body.GetProperty("unplayedCount").GetInt32());

        var first = await PlayAsync(api, api.Ids.Episode1);
        await PostAsync(api.Admin, $"/api/v1/sessions/{first}/stop", new { positionMs = PlaybackSeed.EpisodeDurationMs });

        Assert.Equal(["Episode 2"], Titles((await GetAsync(api.Admin, "/api/v1/library/next-up")).Body));
        Assert.Equal(2, (await GetAsync(api.Admin, $"/api/v1/items/{api.Ids.Series}")).Body.GetProperty("unplayedCount").GetInt32());

        // Halfway through episode 2 it is still next up, and it is in continue watching too
        var second = await PlanAsync(api, api.Ids.Episode2);
        await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{second}/progress", new { positionMs = 1_000_000, isPaused = true });
        Assert.Equal(["Episode 2"], Titles((await GetAsync(api.Admin, "/api/v1/library/next-up")).Body));
        Assert.Equal(["Episode 2"], Titles((await GetAsync(api.Admin, "/api/v1/library/continue-watching")).Body));
    }

    [Fact]
    public async Task WatchState_OutlivesTheItem_AndReturnsWithIt()
    {
        await using var api = await StartAsync();
        var session = await PlayAsync(api, api.Ids.Heat);
        await PostAsync(api.Admin, $"/api/v1/sessions/{session}/stop", new { positionMs = PlaybackSeed.MovieDurationMs });

        // Removed from Radarr and added again: a new item with the same identity key
        Guid readded;
        using (var scope = api.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
            await dbContext.MediaItems.Where(i => i.Id == api.Ids.Heat).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            var item = new Core.Entities.MediaItem
            {
                Type = Core.Enum.MediaItemType.Movie, IdentityKey = "movie:tmdb:949", Title = "Heat", SortTitle = "heat"
            };
            dbContext.MediaItems.Add(item);
            dbContext.ArrLinks.Add(new Core.Entities.ArrLink { MediaItemId = item.Id, InstanceKey = "radarr" });
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
            readded = item.Id;
        }

        var state = (await GetAsync(api.Admin, $"/api/v1/items/{readded}")).Body.GetProperty("userState");
        Assert.Equal((true, 1), (state.GetProperty("isPlayed").GetBoolean(), state.GetProperty("playCount").GetInt32()));
    }

    [Fact]
    public async Task Hls_ForABrowser_SaysWhatChanges_AndListsEverySegment()
    {
        await using var api = await StartAsync();

        var (status, plan, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", Browser);

        Assert.Equal(HttpStatusCode.OK, status);
        var session = plan.GetProperty("sessionId").GetGuid();
        var source = plan.GetProperty("sources")[0];
        Assert.Equal(("hls", $"/api/v1/sessions/{session}/hls/master.m3u8"), (source.GetProperty("mode").GetString(), source.GetProperty("url").GetString()));
        Assert.Equal(1, plan.GetProperty("sources").GetArrayLength());
        Assert.Equal(api.Ids.HeatFullHd, plan.GetProperty("version").GetProperty("fileId").GetGuid());
        var transcode = plan.GetProperty("transcode");
        // The container and AC-3 alone would be a remux; the forced PGS subtitle a browser cannot show makes it an encode
        Assert.Equal(("h264", "aac"), (transcode.GetProperty("video").GetString(), transcode.GetProperty("audio").GetString()));
        Assert.Equal(["container", "audio-codec", "subtitle"], transcode.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));

        var master = await api.Admin.GetAsync($"/api/v1/sessions/{session}/hls/master.m3u8", TestContext.Current.CancellationToken);
        Assert.Equal("application/vnd.apple.mpegurl", master.Content.Headers.ContentType?.MediaType);
        var masterText = await master.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("RESOLUTION=1920x1080\nmain.m3u8", masterText);

        var media = await api.Admin.GetStringAsync($"/api/v1/sessions/{session}/hls/main.m3u8", TestContext.Current.CancellationToken);
        // Each segment carries the version of its tracks, so a browser does not play what it cached of other tracks
        Assert.Matches(@"#EXTINF:6\.000000,\n999\.ts\?v=[0-9a-f]{8}\n#EXT-X-ENDLIST", media);

        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(api.Admin, $"/api/v1/sessions/{session}/hls/1000.ts")).Status);
        // The file is not on disk in this test, so a segment cannot be made
        Assert.Equal(HttpStatusCode.Conflict, (await GetAsync(api.Admin, $"/api/v1/sessions/{session}/hls/0.ts")).Status);

        await PostAsync(api.Admin, $"/api/v1/sessions/{session}/stop", new { positionMs = 0 });
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(api.Admin, $"/api/v1/sessions/{session}/hls/main.m3u8")).Status);
    }

    [Fact]
    public async Task Hls_BurnsInTheForcedImageSubtitle_ABrowserCannotShow_AndStopsWhenTheUserPicksText()
    {
        await using var api = await StartAsync();

        var plan = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", Browser)).Body;

        // English audio and the forced PGS track, which a browser can only see drawn into the picture
        Assert.Equal("fhd-forced", plan.GetProperty("subtitle").GetProperty("streamId").GetString());
        var tracks = plan.GetProperty("tracks").EnumerateArray().ToDictionary(t => t.GetProperty("streamId").GetString()!, t => t.GetProperty("match"));
        Assert.Equal($"/api/v1/files/{api.Ids.HeatFullHd}/subtitles/fhd-dut/vtt", tracks["fhd-dut"].GetProperty("webVttUrl").GetString());
        Assert.Equal(JsonValueKind.Null, tracks["fhd-forced"].GetProperty("webVttUrl").ValueKind);
        var transcode = plan.GetProperty("transcode");
        Assert.Equal(("h264", "fhd-forced"), (transcode.GetProperty("video").GetString(), transcode.GetProperty("burnedSubtitleStreamId").GetString()));
        Assert.Contains("subtitle", transcode.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));

        var session = plan.GetProperty("sessionId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent,
            await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/tracks", new { subtitleStreamId = "fhd-dut" }));
        using var scope = api.Factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AkiraDbContext>().PlaybackSessions
            .SingleAsync(s => s.Id == session, TestContext.Current.CancellationToken);
        var dutch = new MediaStream { Id = "fhd-dut", Kind = StreamKind.Subtitle, Codec = "subrip", IsTextBased = true, IsExternal = true };
        Assert.False(StreamDecision.FromToken(stored.StreamDecision)!.Burns(dutch), "The Dutch SubRip goes as WebVTT, it is not burned");
    }

    [Fact]
    public async Task NowPlaying_ShowsAdminsWhoPlaysWhatAndHow()
    {
        await using var api = await StartAsync();
        var session = await PlanAsync(api, api.Ids.Episode1);
        await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/progress", new { positionMs = 90_000, isPaused = true });

        var playing = (await GetAsync(api.Admin, "/api/v1/sessions")).Body.EnumerateArray().Single();

        Assert.Equal((session, "direct", true, 90_000L), (playing.GetProperty("sessionId").GetGuid(), playing.GetProperty("delivery").GetString(),
            playing.GetProperty("isPaused").GetBoolean(), playing.GetProperty("positionMs").GetInt64()));
        Assert.Equal(("Mayfair Witches", "Episode 1"), (playing.GetProperty("item").GetProperty("seriesTitle").GetString(),
            playing.GetProperty("item").GetProperty("title").GetString()));
        Assert.Equal(("h264", 1080), (playing.GetProperty("version").GetProperty("videoCodec").GetString(),
            playing.GetProperty("version").GetProperty("height").GetInt32()));
        Assert.Equal("aac", playing.GetProperty("audio").GetProperty("codec").GetString());
        Assert.Equal(JsonValueKind.Null, playing.GetProperty("transcoder").ValueKind);

        using var anonymous = api.Factory.CreateAnonymousClient();
        using var kid = api.Factory.CreateClient(await PairAsync(api.Admin, anonymous, "personal", await CreateUserAsync(api.Admin, "Kid")));
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(kid, "/api/v1/sessions")).Status);
    }

    [Fact]
    public async Task Hls_IsOnlyForAClientThatAsks_AndASessionThatPlaysTheFileHasNone()
    {
        await using var api = await StartAsync();

        var plan = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", UhdTv)).Body;

        Assert.Equal(JsonValueKind.Null, plan.GetProperty("transcode").ValueKind);
        Assert.DoesNotContain("hls", plan.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("mode").GetString()));
        var session = plan.GetProperty("sessionId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(api.Admin, $"/api/v1/sessions/{session}/hls/master.m3u8")).Status);
    }

    [Fact]
    public async Task Trickplay_IsMadeForFilesOnDisk_AndServedAsSheetsAndAVttTrack()
    {
        await using var api = await StartAsync(services: services => services.Replace(ServiceDescriptor.Singleton<ITrickplayGenerator, FakeTrickplay>()));
        var episode = Path.Combine(api.MediaRoot, "tv", "Mayfair Witches", "S01E01.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(episode)!);
        await File.WriteAllBytesAsync(episode, [1, 2, 3], TestContext.Current.CancellationToken);

        Guid fileId;
        using (var scope = api.Factory.Services.CreateScope())
        {
            var trickplay = scope.ServiceProvider.GetRequiredService<ITrickplayService>();
            var rounds = 0;
            while (await trickplay.GenerateNextAsync(TestContext.Current.CancellationToken))
            {
                Assert.True(++rounds < 20, "Every file is tried once, then the worker stops");
            }

            fileId = (await scope.ServiceProvider.GetRequiredService<AkiraDbContext>().MediaFiles
                .SingleAsync(f => f.MediaItemId == api.Ids.Episode1, TestContext.Current.CancellationToken)).Id;
        }

        var plan = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode1}/playback-plan", UhdTv)).Body;
        Assert.Equal($"/api/v1/files/{fileId}/trickplay", plan.GetProperty("trickplayUrl").GetString());

        var manifest = (await GetAsync(api.Admin, $"/api/v1/files/{fileId}/trickplay")).Body;
        Assert.Equal((10_000, 270, 3), (manifest.GetProperty("intervalMs").GetInt32(), manifest.GetProperty("count").GetInt32(),
            manifest.GetProperty("sheets").GetArrayLength()));
        var sheet = await api.Admin.GetAsync(manifest.GetProperty("sheets")[1].GetString(), TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode.OK, "image/jpeg"), (sheet.StatusCode, sheet.Content.Headers.ContentType?.MediaType));
        var vtt = await api.Admin.GetStringAsync($"/api/v1/files/{fileId}/trickplay/thumbnails.vtt", TestContext.Current.CancellationToken);
        Assert.Contains($"00:00:10.000 --> 00:00:20.000\n/api/v1/files/{fileId}/trickplay/0.jpg#xywh=320,0,320,180", vtt);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(api.Admin, $"/api/v1/files/{fileId}/trickplay/3.jpg")).Status);

        // The files that are not on disk failed, and have nothing to show
        var episode2 = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode2}/playback-plan", UhdTv)).Body;
        Assert.Equal(JsonValueKind.Null, episode2.GetProperty("trickplayUrl").ValueKind);
    }

    [Fact]
    public async Task ASeasonsSharedAudio_BecomesTheIntroAndCredits_UnlessChaptersNameThem()
    {
        await using var api = await StartAsync(services: services => services.Replace(ServiceDescriptor.Singleton<IAudioFingerprinter, FakeFingerprints>()));
        foreach (var number in new[] { 1, 2, 3 })
        {
            var path = Path.Combine(api.MediaRoot, "tv", "Mayfair Witches", $"S01E0{number}.mkv");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        }

        using (var scope = api.Factory.Services.CreateScope())
        {
            var scanner = scope.ServiceProvider.GetRequiredService<IMarkerScanService>();
            Assert.True(await scanner.ScanNextSeasonAsync(TestContext.Current.CancellationToken));
            Assert.False(await scanner.ScanNextSeasonAsync(TestContext.Current.CancellationToken));
        }

        var markers = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode2}/playback-plan", UhdTv)).Body.GetProperty("markers")
            .EnumerateArray().ToList();

        Assert.Equal([("intro", "fingerprint"), ("credits", "fingerprint")],
            markers.Select(m => (m.GetProperty("kind").GetString(), m.GetProperty("source").GetString())));
        // Episode 2 has its intro 60 seconds in, and its credits start 30 seconds into the last 4 minutes
        Assert.InRange(markers[0].GetProperty("startMs").GetInt64(), 59_700, 60_300);
        Assert.InRange(markers[1].GetProperty("startMs").GetInt64(), PlaybackSeed.EpisodeDurationMs - 210_300, PlaybackSeed.EpisodeDurationMs - 209_700);
    }

    // Every episode: its own audio, a shared 40 second intro at a different place per episode, and shared credits
    private sealed class FakeFingerprints : IAudioFingerprinter
    {
        private static readonly int Forty = (int)(40 / AudioMatch.SecondsPerPoint);

        public Task<uint[]> FingerprintAsync(string path, int audioIndex, TimeSpan start, TimeSpan length, CancellationToken cancellationToken)
        {
            var episode = int.Parse(Path.GetFileNameWithoutExtension(path)[^1..]);
            var count = (int)(length.TotalSeconds / AudioMatch.SecondsPerPoint);
            var points = Noise(episode * 100 + (start == TimeSpan.Zero ? 0 : 1), count);
            var at = (int)((start == TimeSpan.Zero ? episode * 30 : 30) / AudioMatch.SecondsPerPoint);
            Noise(start == TimeSpan.Zero ? 7 : 8, Forty).CopyTo(points, at);
            return Task.FromResult(points);
        }

        private static uint[] Noise(int seed, int count)
        {
            var random = new Random(seed);
            return Enumerable.Range(0, count).Select(_ => (uint)random.NextInt64(0, uint.MaxValue)).ToArray();
        }
    }

    private sealed class FakeTrickplay : ITrickplayGenerator
    {
        public async Task<TrickplayLayout> GenerateAsync(string videoPath, int? videoWidth, int? videoHeight, long durationMs, string folder,
            CancellationToken cancellationToken)
        {
            var layout = new TrickplayLayout(10_000, 320, TrickplayLayout.HeightFor(320, videoWidth, videoHeight), 10, 10,
                TrickplayLayout.CountFor(durationMs, 10_000));
            Directory.CreateDirectory(folder);
            for (var sheet = 0; sheet < layout.SheetCount; sheet++)
            {
                await File.WriteAllBytesAsync(Path.Combine(folder, $"{sheet}.jpg"), [0xFF, 0xD8, 0xFF], cancellationToken);
            }

            return layout;
        }
    }

    [Fact]
    public async Task Stream_ServesRanges_AndSaysWhenTheFileIsNotOnDisk()
    {
        await using var api = await StartAsync();
        var path = Path.Combine(api.MediaRoot, "movies", "Heat (1995)", "Heat.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 100).Select(i => (byte)i).ToArray(), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.ChangeExtension(path, ".nl.srt"), "1\n00:00:01,000 --> 00:00:02,000\nHallo\n",
            TestContext.Current.CancellationToken);

        // A plan nobody played ends unheard and leaves the watch state; fetching the file is what starts a plan
        var abandoned = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", SdrTablet)).Body.GetProperty("sessionId").GetGuid();
        Assert.Equal(0, (await PostAsync(api.Admin, $"/api/v1/sessions/{abandoned}/stop", new { positionMs = 3_000_000 })).Body
            .GetProperty("positionMs").GetInt64());
        var session = (await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Heat}/playback-plan", SdrTablet)).Body.GetProperty("sessionId").GetGuid();
        using var asked = new HttpRequestMessage(HttpMethod.Head, $"/api/v1/files/{api.Ids.HeatFullHd}/stream");
        await api.Admin.SendAsync(asked, TestContext.Current.CancellationToken);
        Assert.Empty(await PlaybackEventsAsync(api));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/{api.Ids.HeatFullHd}/stream");
        request.Headers.Range = new RangeHeaderValue(10, 19);
        var partial = await api.Admin.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal([(EventTypes.PlaybackStarted, session)], await PlaybackEventsAsync(api));
        Assert.Equal(Enumerable.Range(10, 10).Select(i => (byte)i), await partial.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("video/x-matroska", partial.Content.Headers.ContentType?.MediaType);

        // A player that kept the tag and resumes gets the same file or nothing stale
        var tag = partial.Headers.ETag!;
        using var unchanged = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/{api.Ids.HeatFullHd}/stream");
        unchanged.Headers.IfNoneMatch.Add(tag);
        Assert.Equal(HttpStatusCode.NotModified, (await api.Admin.SendAsync(unchanged, TestContext.Current.CancellationToken)).StatusCode);
        using var head = new HttpRequestMessage(HttpMethod.Head, $"/api/v1/files/{api.Ids.HeatFullHd}/stream");
        var headResponse = await api.Admin.SendAsync(head, TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode.OK, 100L, "bytes"),
            (headResponse.StatusCode, headResponse.Content.Headers.ContentLength, headResponse.Headers.AcceptRanges.Single()));

        // Saving the file is not watching it
        var download = await api.Admin.GetAsync($"/api/v1/files/{api.Ids.HeatFullHd}/download", TestContext.Current.CancellationToken);
        Assert.Equal(("attachment", "Heat.mkv", 100L),
            (download.Content.Headers.ContentDisposition?.DispositionType, download.Content.Headers.ContentDisposition?.FileName,
                download.Content.Headers.ContentLength));
        Assert.Equal([(EventTypes.PlaybackStarted, session)], await PlaybackEventsAsync(api));

        var subtitle = await api.Admin.GetAsync($"/api/v1/files/{api.Ids.HeatFullHd}/subtitles/fhd-dut", TestContext.Current.CancellationToken);
        Assert.Equal("application/x-subrip", subtitle.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Hallo", await subtitle.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var (missing, body) = await GetAsync(api.Admin, $"/api/v1/files/{api.Ids.HeatUhd}/stream");
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Contains("path mappings of instance radarr-4k", body.GetProperty("detail").GetString());

        using var anonymous = api.Factory.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(anonymous, $"/api/v1/files/{api.Ids.HeatFullHd}/stream")).Status);
    }

    private static (string? StreamId, string Reason) Pick(JsonElement plan, string kind)
    {
        var pick = plan.GetProperty(kind);
        return (pick.GetProperty("streamId").GetString(), pick.GetProperty("reason").GetString()!);
    }

    [Fact]
    public async Task Plan_ForAFileNotProbedYet_ProbesItFirst_SoItsAc3IsEncodedForABrowser()
    {
        await using var api = await StartAsync(services: services => services.Replace(ServiceDescriptor.Singleton<IFfprobeRunner, HevcAc3Ffprobe>()));
        var path = Path.Combine(api.MediaRoot, "tv", "Mayfair Witches", "S01E03.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, [1], TestContext.Current.CancellationToken);
        using (var scope = api.Factory.Services.CreateScope())
        {
            // Just imported: the sync added the file, the probe worker has not been to it
            var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
            var file = await dbContext.MediaFiles.Include(f => f.Streams)
                .SingleAsync(f => f.MediaItemId == api.Ids.Episode3, TestContext.Current.CancellationToken);
            dbContext.MediaStreams.RemoveRange(file.Streams);
            (file.ProbeStatus, file.DurationMs) = (ProbeStatus.Pending, null);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var (status, plan, _) = await PostAsync(api.Admin, $"/api/v1/items/{api.Ids.Episode3}/playback-plan", Chromium);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(("ac3", 6), (plan.GetProperty("audio").GetProperty("match").GetProperty("codec").GetString(),
            plan.GetProperty("audio").GetProperty("match").GetProperty("channels").GetInt32()));
        var transcode = plan.GetProperty("transcode");
        Assert.Equal(("copy", "aac", "audio-codec"), (transcode.GetProperty("video").GetString(), transcode.GetProperty("audio").GetString(),
            transcode.GetProperty("reasons")[0].GetString()));
        var events = (await GetAsync(api.Admin, "/api/v1/events?since=0")).Body.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()).ToList();
        Assert.Contains("library.item.updated", events);
    }

    private static List<string> Titles(JsonElement items) =>
        items.EnumerateArray().Select(item => item.GetProperty("title").GetString()!).ToList();

    private static async Task<Guid> PlanAsync(StartedApi api, Guid itemId)
    {
        var (status, plan, _) = await PostAsync(api.Admin, $"/api/v1/items/{itemId}/playback-plan", UhdTv);
        Assert.Equal(HttpStatusCode.OK, status);
        return plan.GetProperty("sessionId").GetGuid();
    }

    private static async Task<List<(string? Type, Guid Session)>> PlaybackEventsAsync(StartedApi api) =>
        (await GetAsync(api.Admin, "/api/v1/events?since=0")).Body.GetProperty("items").EnumerateArray()
            .Where(e => e.GetProperty("type").GetString()!.StartsWith("playback.", StringComparison.Ordinal))
            .Select(e => (e.GetProperty("type").GetString(), e.GetProperty("payload").GetProperty("sessionId").GetGuid()))
            .ToList();

    // A player reports where it is as it starts playing, which starts the session
    private static async Task<Guid> PlayAsync(StartedApi api, Guid itemId)
    {
        var session = await PlanAsync(api, itemId);
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(api.Admin, HttpMethod.Post, $"/api/v1/sessions/{session}/progress",
            new { positionMs = 0, isPaused = false }));
        return session;
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var buffer = new byte[64 * 1024];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count)).RootElement.Clone();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Response)> PostLikePutAsync(
        HttpClient client, string url, object body)
    {
        var response = await client.PutAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone(), response);
    }

    private async Task<StartedApi> StartAsync(bool clientMappings = true, Dictionary<string, string>? extra = null,
        Action<IServiceCollection>? services = null)
    {
        var (options, database) = await CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var mediaRoot = Directory.CreateTempSubdirectory("akira_media_").FullName.Replace('\\', '/');
        var settings = clientMappings
            ? new Dictionary<string, string>
            {
                ["Playback:ClientPathMappings:0:From"] = mediaRoot,
                ["Playback:ClientPathMappings:0:To"] = "nfs://nas/media"
            }
            : [];
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        settings.TryAdd("Trickplay:Path", Path.Combine(mediaRoot, "trickplay"));
        var factory = new AkiraApiFactory(options)
        {
            Settings = settings,
            TestServices = services,
            Instances =
            [
                new ArrInstanceOptions { Key = "radarr", Name = "Radarr", Kind = ArrKind.Radarr, Url = "http://radarr", ApiKey = "k" },
                new ArrInstanceOptions { Key = "radarr-4k", Name = "Radarr 4K", Kind = ArrKind.Radarr, Url = "http://radarr4k", ApiKey = "k" },
                new ArrInstanceOptions { Key = "sonarr", Name = "Sonarr", Kind = ArrKind.Sonarr, Url = "http://sonarr", ApiKey = "k" }
            ]
        };

        PlaybackSeed.Ids ids;
        using (var scope = factory.Services.CreateScope())
        {
            ids = await PlaybackSeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AkiraDbContext>(), mediaRoot,
                TestContext.Current.CancellationToken);
        }

        return new StartedApi(factory, factory.CreateClient(), ids, mediaRoot, database);
    }

    protected sealed record StartedApi(AkiraApiFactory Factory, HttpClient Admin, PlaybackSeed.Ids Ids, string MediaRoot, IAsyncDisposable? Database)
        : IAsyncDisposable
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

    // ffprobe's real answer for an HEVC episode whose only audio is AC-3 5.1, whatever file it is asked about
    private sealed class HevcAc3Ffprobe : IFfprobeRunner
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        public Task<string> GetVersionAsync(CancellationToken cancellationToken) => Task.FromResult("ffprobe test");

        public async Task<FfprobeOutput> ProbeAsync(string path, CancellationToken cancellationToken) =>
            JsonSerializer.Deserialize<FfprobeOutput>(
                await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ffprobe-hevc-ac3.json"), cancellationToken), Options)!;

        public Task<FfprobeOutput> ProbeSubtitleAsync(string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
