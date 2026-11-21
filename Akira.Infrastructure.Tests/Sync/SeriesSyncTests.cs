using Microsoft.EntityFrameworkCore;
using Akira.Core.Enum;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Sonarr;
using Akira.Infrastructure.Sync;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Sync;

public abstract class SeriesSyncTests
{
    private const int SeriesId = 6;
    private const string SeriesKey = "series:tvdb:267441";

    private static readonly ArrInstanceOptions Sonarr =
        FakeArrClientFactory.Instance("sonarr", ArrKind.Sonarr, ("/anime", "/data/media/anime"));

    private static readonly ArrInstanceOptions Sonarr4K =
        FakeArrClientFactory.Instance("sonarr-4k", ArrKind.Sonarr, ("/anime", "/data/media/anime-4k"));

    protected abstract Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task FirstSync_BuildsSeriesSeasonsAndEpisodes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);

        var report = await SyncHarness.SyncAsync(database, Clients(("sonarr", RealSonarr())), [Sonarr], cancellationToken);

        // 1 series, Specials + 2 seasons, 64 episodes
        Assert.Equal((SyncStatus.Succeeded, 68), (report.Instances[0].Status, report.Instances[0].Added));

        await using var context = database.CreateAkiraContext();
        var series = await context.MediaItems.Include(i => i.Images).Include(i => i.Links)
            .SingleAsync(i => i.IdentityKey == SeriesKey, cancellationToken);
        Assert.Equal((MediaItemType.Series, "The Devil Is a Part-Timer!", "jpn"), (series.Type, series.Title, series.OriginalLanguage));
        Assert.Equal((7.6, "TV-14"), (series.Rating, series.Certification));
        Assert.Equal(("sonarr", 6), (Assert.Single(series.Links).InstanceKey, series.Links[0].ArrId));
        Assert.Contains(series.Images, image => image is { Kind: ImageKind.Poster, ArrFileName: "poster.jpg", ArrId: 6 });

        var seasons = await context.MediaItems.Include(i => i.Links).Where(i => i.ParentId == series.Id)
            .OrderBy(i => i.SeasonNumber).ToListAsync(cancellationToken);
        Assert.Equal(["Specials", "Season 1", "Season 2"], seasons.Select(season => season.Title));
        Assert.Null(Assert.Single(seasons[1].Links).ArrId);

        var episode = await context.MediaItems.Include(i => i.Files).Include(i => i.Images).Include(i => i.Links)
            .SingleAsync(i => i.IdentityKey == SeriesKey + ":s2:e1", cancellationToken);
        Assert.Equal(seasons[2].Id, episode.ParentId);
        Assert.Equal(("The Devil Screams in Sasazuka", 24, 8884747), (episode.Title, episode.RuntimeMinutes, episode.TvdbId));
        Assert.Equal(256, Assert.Single(episode.Links).ArrId);
        Assert.Equal(new DateOnly(2022, 7, 14), episode.AirDate);
        var file = Assert.Single(episode.Files);
        Assert.StartsWith("/data/media/anime/The Devil Is a Part-Timer/", file.Path);
        Assert.Equal(file.AddedAt, episode.AddedAt);
        var still = Assert.Single(episode.Images);
        Assert.Equal((ImageKind.Still, (string?)null, (int?)null), (still.Kind, still.ArrFileName, still.ArrId));
        Assert.StartsWith("https://artworks.thetvdb.com/", still.RemoteUrl);

        Assert.Equal(3, await context.MediaFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Refresh_AfterAnEpisodeFileWasDeleted_UpdatesThatEpisode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var sonarr = RealSonarr();
        var clients = Clients(("sonarr", sonarr));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        sonarr.Episodes[SeriesId] = sonarr.Episodes[SeriesId]
            .Select(episode => episode.Id == 256 ? episode with { HasFile = false, EpisodeFileId = null } : episode)
            .ToList();
        var report = await SyncHarness.RefreshAsync(database, clients, [Sonarr], "sonarr", SeriesId, cancellationToken);

        var updated = Assert.Single(report.Instances[0].UpdatedItems);
        await using var context = database.CreateAkiraContext();
        var seriesId = await context.MediaItems.Where(i => i.IdentityKey == SeriesKey).Select(i => i.Id).SingleAsync(cancellationToken);
        Assert.Equal((SeriesKey + ":s2:e1", MediaItemType.Episode, (Guid?)seriesId), (updated.IdentityKey, updated.Type, updated.SeriesId));
        Assert.Empty(report.Instances[0].ReplacedFiles);
        Assert.Equal(2, await context.MediaFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Refresh_OfASeriesSonarrDeleted_RemovesItsSeasonsAndEpisodes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var sonarr = RealSonarr();
        var clients = Clients(("sonarr", sonarr));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        sonarr.Series = sonarr.Series.Where(series => series.Id != SeriesId).ToList();
        var report = await SyncHarness.RefreshAsync(database, clients, [Sonarr], "sonarr", SeriesId, cancellationToken);

        Assert.Equal((68, 68), (report.Instances[0].Unlinked, report.RemovedItems));
        Assert.Contains(report.Removed, change => change is { IdentityKey: SeriesKey, Type: MediaItemType.Series });
        await using var context = database.CreateAkiraContext();
        Assert.False(await context.MediaItems.AnyAsync(i => i.IdentityKey.StartsWith(SeriesKey), cancellationToken));
    }

    [Fact]
    public async Task Refresh_OfAnUnknownSeries_DoesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("sonarr", RealSonarr()));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        var report = await SyncHarness.RefreshAsync(database, clients, [Sonarr], "sonarr", 4242, cancellationToken);

        Assert.Equal((SyncStatus.Succeeded, 0, 0, 0, 0),
            (report.Instances[0].Status, report.Instances[0].Added, report.Instances[0].Updated, report.Instances[0].Unlinked, report.RemovedItems));
    }

    [Fact]
    public async Task SecondSync_WithSameData_ChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("sonarr", RealSonarr()));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        var report = await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        var result = report.Instances[0];
        Assert.Equal((SyncStatus.Succeeded, 0, 0, 0, 0), (result.Status, result.Added, result.Updated, result.Unlinked, report.RemovedItems));
    }

    [Fact]
    public async Task FailedEpisodeList_KeepsThatSeriesUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("sonarr", RealSonarr()));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        clients.Sonarr["sonarr"].FailingSeries.Add(SeriesId);
        var report = await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        Assert.Equal((SyncStatus.PartiallySucceeded, 0, 0), (report.Instances[0].Status, report.Instances[0].Unlinked, report.RemovedItems));
        Assert.Contains("did not answer in time", Assert.Single(report.Instances[0].Problems));
        await using var context = database.CreateAkiraContext();
        Assert.Equal(68, await context.MediaItems.CountAsync(cancellationToken));
        Assert.Equal(3, await context.MediaFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task DeletedEpisodeFile_RemovesVersionButKeepsEpisode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var sonarr = RealSonarr();
        var clients = Clients(("sonarr", sonarr));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        sonarr.Episodes[SeriesId] = sonarr.Episodes[SeriesId]
            .Select(episode => episode.Id == 256 ? episode with { HasFile = false, EpisodeFileId = 0 } : episode)
            .ToList();
        sonarr.Files[SeriesId] = sonarr.Files[SeriesId].Where(file => file.Id != 23).ToList();
        var report = await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        Assert.Equal((1, 0), (report.Instances[0].Updated, report.RemovedItems));
        await using var context = database.CreateAkiraContext();
        var episode = await context.MediaItems.Include(i => i.Files).SingleAsync(i => i.IdentityKey == SeriesKey + ":s2:e1", cancellationToken);
        Assert.Empty(episode.Files);
    }

    [Fact]
    public async Task RemovedSeries_DeletesSeriesSeasonsAndEpisodes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var sonarr = RealSonarr();
        var clients = Clients(("sonarr", sonarr));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        sonarr.Series = [];
        var report = await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        Assert.Equal(68, report.RemovedItems);
        await using var context = database.CreateAkiraContext();
        Assert.Equal(0, await context.MediaItems.CountAsync(cancellationToken));
        Assert.Equal(0, await context.MediaFiles.CountAsync(cancellationToken));
        Assert.Equal(0, await context.ArrLinks.CountAsync(cancellationToken));
        Assert.Equal(0, await context.MediaImages.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task EpisodeMissingFromList_IsDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var sonarr = RealSonarr();
        var clients = Clients(("sonarr", sonarr));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        sonarr.Episodes[SeriesId] = sonarr.Episodes[SeriesId].Where(episode => episode.Id != 256).ToList();
        var report = await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        Assert.Equal(1, report.RemovedItems);
        await using var context = database.CreateAkiraContext();
        Assert.False(await context.MediaItems.AnyAsync(i => i.IdentityKey == SeriesKey + ":s2:e1", cancellationToken));
    }

    [Fact]
    public async Task AnOddOrBrokenSeries_CostsOnlyItself()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var sonarr = RealSonarr();
        var clients = Clients(("sonarr", sonarr));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        // A season listed twice and a certification longer than its column, which MariaDB refuses with the whole save;
        // and a series whose episodes throw what no upstream call should
        var real = sonarr.Series.Single();
        var odd = real with
        {
            Id = 7,
            TvdbId = 7,
            Title = "Odd Show",
            Certification = new string('X', 30),
            Seasons = [new SonarrSeason(1, []), new SonarrSeason(1, [])]
        };
        sonarr.Series = [real, odd];
        sonarr.Episodes[7] = sonarr.Episodes[SeriesId].Select(episode => episode with { SeriesId = 7 }).ToList();
        sonarr.BrokenSeries[SeriesId] = new InvalidOperationException("Sequence contains more than one element");
        var report = await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        var result = report.Instances[0];
        Assert.Equal((SyncStatus.PartiallySucceeded, 0, 0), (result.Status, result.Unlinked, report.RemovedItems));
        Assert.Contains(result.Problems, problem => problem.Contains("more than one element"));
        await using var context = database.CreateAkiraContext();
        Assert.Equal(68, await context.MediaItems.CountAsync(i => i.IdentityKey.StartsWith(SeriesKey), cancellationToken));
        var series = await context.MediaItems.SingleAsync(i => i.IdentityKey == "series:tvdb:7", cancellationToken);
        Assert.Null(series.Certification);
        Assert.Equal(68, await context.MediaItems.CountAsync(i => i.IdentityKey.StartsWith("series:tvdb:7"), cancellationToken));
    }

    [Fact]
    public async Task ARefreshDuringTheSync_IsNotUndoneByItsOlderList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var sonarr = RealSonarr();
        var clients = Clients(("sonarr", sonarr));
        await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken);

        // The sync has its list when the series is deleted in Sonarr and its webhook refreshed. Asked before the gate,
        // so the refresh is not stuck behind it, and asked again after, so the older list does not bring the series back.
        var syncLock = new SyncLock();
        sonarr.AfterList = async () =>
        {
            sonarr.Series = [];
            await SyncHarness.RefreshAsync(database, clients, [Sonarr], "sonarr", SeriesId, cancellationToken, syncLock);
        };
        var report = await SyncHarness.SyncAsync(database, clients, [Sonarr], cancellationToken, syncLock)
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        Assert.Equal((SyncStatus.Succeeded, 0), (report.Instances[0].Status, report.Instances[0].Added));
        await using var context = database.CreateAkiraContext();
        Assert.Equal(0, await context.MediaItems.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task SameSeriesInTwoInstances_GivesEpisodesTwoVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);

        var report = await SyncHarness.SyncAsync(database,
            Clients(("sonarr", RealSonarr()), ("sonarr-4k", RealSonarr())), [Sonarr, Sonarr4K], cancellationToken);

        Assert.Equal([68, 0], report.Instances.Select(result => result.Added));
        await using var context = database.CreateAkiraContext();
        Assert.Equal(68, await context.MediaItems.CountAsync(cancellationToken));
        var episode = await context.MediaItems.Include(i => i.Files).SingleAsync(i => i.IdentityKey == SeriesKey + ":s2:e1", cancellationToken);
        Assert.Equal(["sonarr", "sonarr-4k"], episode.Files.Select(file => file.InstanceKey).Order());
        Assert.Contains(episode.Files, file => file.Path.StartsWith("/data/media/anime-4k/"));
    }

    private async Task<TestDatabase> CreateMigratedDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = await CreateDatabaseAsync(cancellationToken);
        database.Migrate();
        return database;
    }

    private static FakeArrClientFactory Clients(params (string Key, FakeSonarrClient Client)[] instances)
    {
        var clients = new FakeArrClientFactory();
        foreach (var (key, client) in instances)
        {
            clients.Sonarr[key] = client;
        }

        return clients;
    }

    private static FakeSonarrClient RealSonarr()
    {
        var sonarr = new FakeSonarrClient { Series = Fixtures.Load<List<SonarrSeries>>("sonarr-series.json") };
        sonarr.Episodes[SeriesId] = Fixtures.Load<List<SonarrEpisode>>("sonarr-episodes-6.json");
        sonarr.Files[SeriesId] = Fixtures.Load<List<SonarrEpisodeFile>>("sonarr-episodefiles-6.json");
        return sonarr;
    }
}
