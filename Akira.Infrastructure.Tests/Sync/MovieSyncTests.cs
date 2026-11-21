using Microsoft.EntityFrameworkCore;
using Akira.Core.Enum;
using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Arr;
using Akira.Infrastructure.Integrations.Radarr;
using Akira.Infrastructure.Sync;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Sync;

public abstract class MovieSyncTests
{
    private static readonly ArrInstanceOptions Radarr =
        FakeArrClientFactory.Instance("radarr", ArrKind.Radarr, ("/movies", "/data/media/movies"));

    private static readonly ArrInstanceOptions Radarr4K =
        FakeArrClientFactory.Instance("radarr-4k", ArrKind.Radarr, ("/movies", "/data/media/movies-4k"));

    protected abstract Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task FirstSync_ImportsMoviesFilesAndImages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));

        var report = await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        var result = Assert.Single(report.Instances);
        Assert.Equal(("radarr", SyncStatus.Succeeded, 2, 0, 0), (result.InstanceKey, result.Status, result.Added, result.Updated, result.Unlinked));

        await using var context = database.CreateAkiraContext();
        var movie = await context.MediaItems.Include(i => i.Files).Include(i => i.Images).Include(i => i.Links)
            .SingleAsync(i => i.IdentityKey == "movie:tmdb:170", cancellationToken);
        Assert.Equal((MediaItemType.Movie, "28 Days Later", "28 days later"), (movie.Type, movie.Title, movie.SortTitle));
        Assert.Equal((2002, 113, "tt0289043", "eng"), (movie.Year, movie.RuntimeMinutes, movie.ImdbId, movie.OriginalLanguage));
        Assert.Equal((7.5, 461000, 7.2, "mWEhfF27O0c", "Horror, Thriller, Science Fiction", "R"),
            (movie.ImdbRating, movie.ImdbVotes, movie.TmdbRating, movie.TrailerYoutubeId, movie.Genres, movie.Certification));
        var link = Assert.Single(movie.Links);
        Assert.Equal(("radarr", 1), (link.InstanceKey, link.ArrId));
        var file = Assert.Single(movie.Files);
        Assert.Equal("/data/media/movies/28 Days Later (2002)/28 Days Later 2002 Bluray-1080p.mp4", file.Path);
        Assert.Equal(("radarr", "mp4", 2, 1665482211L), (file.InstanceKey, file.Container, file.ArrFileId, file.SizeBytes));

        var poster = movie.Images.Single(image => image.Kind == ImageKind.Poster);
        Assert.Equal(("radarr", 1, "poster.jpg"), (poster.InstanceKey, poster.ArrId, poster.ArrFileName));
        Assert.StartsWith("https://image.tmdb.org/", poster.RemoteUrl);
    }

    [Fact]
    public async Task SecondSync_WithSameData_ChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        var report = await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        var result = Assert.Single(report.Instances);
        Assert.Equal((SyncStatus.Succeeded, 0, 0, 0, 0), (result.Status, result.Added, result.Updated, result.Unlinked, report.RemovedItems));
    }

    [Fact]
    public async Task Upgrade_ReplacesFileButKeepsItem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);
        var (itemId, fileId) = await GetIdsAsync(database, "movie:tmdb:170", cancellationToken);

        clients.Radarr["radarr"].Movies = RealMovies()
            .Select(movie => movie.TmdbId == 170
                ? movie with
                {
                    MovieFile = new RadarrMovieFile(99, "/movies/28 Days Later (2002)/28 Days Later 2002 Remux-2160p.mkv",
                        60_000_000_000, DateTimeOffset.UtcNow)
                }
                : movie)
            .ToList();
        var report = await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        Assert.Equal((0, 1), (report.Instances[0].Added, report.Instances[0].Updated));
        var (newItemId, newFileId) = await GetIdsAsync(database, "movie:tmdb:170", cancellationToken);
        Assert.Equal(itemId, newItemId);
        Assert.NotEqual(fileId, newFileId);
        Assert.Equal(new FileReplacement(itemId, "radarr", fileId!.Value, newFileId!.Value), Assert.Single(report.Instances[0].ReplacedFiles));
    }

    [Fact]
    public async Task Refresh_OfOneMovie_LeavesTheOthersAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);
        var (itemId, fileId) = await GetIdsAsync(database, "movie:tmdb:170", cancellationToken);

        // Radarr upgraded movie 1 and someone renamed movie 2, but the webhook is about movie 1
        clients.Radarr["radarr"].Movies = RealMovies()
            .Select(movie => movie.Id == 1
                ? movie with { MovieFile = new RadarrMovieFile(99, "/movies/28 Days Later (2002)/28 Days Later Remux-2160p.mkv", 1, null) }
                : movie with { Title = "Renamed" })
            .ToList();
        var report = await SyncHarness.RefreshAsync(database, clients, [Radarr], "radarr", 1, cancellationToken);

        var result = Assert.Single(report.Instances);
        Assert.Equal((SyncStatus.Succeeded, 0, 1, 0), (result.Status, result.Added, result.Updated, report.RemovedItems));
        Assert.Equal((itemId, MediaItemType.Movie, "movie:tmdb:170"),
            (result.UpdatedItems[0].ItemId, result.UpdatedItems[0].Type, result.UpdatedItems[0].IdentityKey));
        Assert.Equal(fileId, Assert.Single(result.ReplacedFiles).PreviousFileId);

        await using var context = database.CreateAkiraContext();
        Assert.Equal("28 Weeks Later", await context.MediaItems.Where(i => i.IdentityKey == "movie:tmdb:1562").Select(i => i.Title)
            .SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task Refresh_OfANewMovie_AddsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies().Where(movie => movie.Id == 2).ToList()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        clients.Radarr["radarr"].Movies = RealMovies();
        var report = await SyncHarness.RefreshAsync(database, clients, [Radarr], "radarr", 1, cancellationToken);

        var added = Assert.Single(report.Instances[0].AddedItems);
        Assert.Equal(("movie:tmdb:170", (Guid?)null, (Guid?)null), (added.IdentityKey, added.ParentId, added.SeriesId));
    }

    [Fact]
    public async Task Refresh_OfAMovieRadarrDeleted_RemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);
        var (itemId, _) = await GetIdsAsync(database, "movie:tmdb:170", cancellationToken);

        clients.Radarr["radarr"].Movies = RealMovies().Where(movie => movie.Id != 1).ToList();
        var report = await SyncHarness.RefreshAsync(database, clients, [Radarr], "radarr", 1, cancellationToken);

        Assert.Equal(1, report.Instances[0].Unlinked);
        Assert.Equal(itemId, Assert.Single(report.Removed).ItemId);
        await using var context = database.CreateAkiraContext();
        Assert.Equal(["movie:tmdb:1562"], await context.MediaItems.Select(i => i.IdentityKey).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task Refresh_OfAMovieTheOtherRadarrStillHas_KeepsTheItem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()), ("radarr-4k", Upgraded4K(RealMovies())));
        await SyncHarness.SyncAsync(database, clients, [Radarr, Radarr4K], cancellationToken);

        clients.Radarr["radarr"].Movies = [];
        var report = await SyncHarness.RefreshAsync(database, clients, [Radarr, Radarr4K], "radarr", 1, cancellationToken);

        Assert.Equal((1, 0), (report.Instances[0].Unlinked, report.RemovedItems));
        await using var context = database.CreateAkiraContext();
        var files = await context.MediaFiles.Where(f => f.MediaItem.IdentityKey == "movie:tmdb:170").Select(f => f.InstanceKey)
            .ToListAsync(cancellationToken);
        Assert.Equal(["radarr-4k"], files);
    }

    [Fact]
    public async Task Refresh_WhenRadarrFails_ChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        clients.Radarr["radarr"].Failure = new UpstreamException("Radarr", UpstreamFailure.Unauthorized, "Radarr rejected the API key.");
        var report = await SyncHarness.RefreshAsync(database, clients, [Radarr], "radarr", 1, cancellationToken);

        Assert.Equal((SyncStatus.Failed, 0), (report.Instances[0].Status, report.RemovedItems));
    }

    [Fact]
    public async Task NewAndDroppedImages_OnExistingItem_AreSaved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        clients.Radarr["radarr"].Movies = RealMovies()
            .Select(movie => movie.TmdbId == 170
                ? movie with { Images = [new ArrImage("clearlogo", "/MediaCover/1/clearlogo.png", null)] }
                : movie)
            .ToList();
        var report = await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        Assert.Equal(1, report.Instances[0].Updated);
        await using var context = database.CreateAkiraContext();
        var logo = Assert.Single(await context.MediaImages
            .Where(image => image.MediaItem.IdentityKey == "movie:tmdb:170").ToListAsync(cancellationToken));
        Assert.Equal((ImageKind.Logo, "clearlogo.png", (string?)null), (logo.Kind, logo.ArrFileName, logo.RemoteUrl));
    }

    [Fact]
    public async Task RemovedMovie_IsDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        clients.Radarr["radarr"].Movies = RealMovies().Where(movie => movie.TmdbId != 170).ToList();
        var report = await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        Assert.Equal((1, 1), (report.Instances[0].Unlinked, report.RemovedItems));
        await using var context = database.CreateAkiraContext();
        Assert.False(await context.MediaItems.AnyAsync(i => i.IdentityKey == "movie:tmdb:170", cancellationToken));
        Assert.Equal(1, await context.MediaFiles.CountAsync(cancellationToken));
        Assert.Equal(1, await context.ArrLinks.CountAsync(cancellationToken));
        Assert.Equal(2, await context.MediaImages.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task FailedRadarr_RemovesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        clients.Radarr["radarr"].Failure = new UpstreamException("Radarr", UpstreamFailure.Unauthorized, "Radarr rejected the API key.");
        var report = await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        Assert.Equal(SyncStatus.Failed, report.Instances[0].Status);
        Assert.Equal("Radarr rejected the API key.", Assert.Single(report.Instances[0].Problems));
        Assert.Equal(0, report.RemovedItems);
        await using var context = database.CreateAkiraContext();
        Assert.Equal(2, await context.MediaFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task EmptyRadarr_RemovesEverything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        clients.Radarr["radarr"].Movies = [];
        var report = await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        Assert.Equal(2, report.RemovedItems);
    }

    [Fact]
    public async Task MovieWithoutFile_IsImportedWithoutVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies().Select(movie => movie with { HasFile = false, MovieFile = null }).ToList()));

        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        await using var context = database.CreateAkiraContext();
        Assert.Equal(2, await context.MediaItems.CountAsync(cancellationToken));
        Assert.Equal(0, await context.MediaFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task InvalidMovie_IsSkippedAndReported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var movies = RealMovies();
        movies.Add(movies[0] with { Id = 77, Title = " ", TmdbId = 1 });

        var report = await SyncHarness.SyncAsync(database, Clients(("radarr", movies)), [Radarr], cancellationToken);

        Assert.Equal((SyncStatus.PartiallySucceeded, 2), (report.Instances[0].Status, report.Instances[0].Added));
        Assert.Contains("77", Assert.Single(report.Instances[0].Problems));
    }

    [Fact]
    public async Task SameMovieInTwoInstances_IsOneItemWithTwoVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()), ("radarr-4k", Upgraded4K(RealMovies())));

        var report = await SyncHarness.SyncAsync(database, clients, [Radarr, Radarr4K], cancellationToken);

        Assert.Equal([2, 0], report.Instances.Select(result => result.Added));
        await using var context = database.CreateAkiraContext();
        Assert.Equal(2, await context.MediaItems.CountAsync(cancellationToken));
        var movie = await context.MediaItems.Include(i => i.Files).Include(i => i.Links)
            .SingleAsync(i => i.IdentityKey == "movie:tmdb:170", cancellationToken);
        Assert.Equal(["radarr", "radarr-4k"], movie.Links.Select(link => link.InstanceKey).Order());
        Assert.Equal(
            ["/data/media/movies-4k/28 Days Later (2002)/28 Days Later 2002 Remux-2160p.mkv",
             "/data/media/movies/28 Days Later (2002)/28 Days Later 2002 Bluray-1080p.mp4"],
            movie.Files.Select(file => file.Path).Order());

        // Added when the first copy arrived, a later 4K copy does not make it new
        Assert.Equal(new DateTimeOffset(2026, 5, 15, 9, 37, 17, TimeSpan.Zero), movie.AddedAt);
    }

    [Fact]
    public async Task AccentedTitleInTwoInstances_IsDescribedByTheFirst_AndNotUpdatedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        static List<RadarrMovie> Accented(List<RadarrMovie> movies, string overview) => movies
            .Select(movie => movie.TmdbId == 170 ? movie with { Title = "Écran Noir", SortTitle = "écran noir", Overview = overview } : movie)
            .ToList();
        var clients = Clients(("radarr", Accented(RealMovies(), "From the first Radarr.")),
            ("radarr-4k", Accented(Upgraded4K(RealMovies()), "From the 4K Radarr.")));
        await SyncHarness.SyncAsync(database, clients, [Radarr, Radarr4K], cancellationToken);

        var report = await SyncHarness.SyncAsync(database, clients, [Radarr, Radarr4K], cancellationToken);

        // Stored folded, compared folded; and the second instance does not write its own words over the first's
        Assert.Equal([0, 0], report.Instances.Select(result => result.Updated));
        await using var context = database.CreateAkiraContext();
        var movie = await context.MediaItems.SingleAsync(i => i.IdentityKey == "movie:tmdb:170", cancellationToken);
        Assert.Equal(("Écran Noir", "ecran noir", "From the first Radarr."), (movie.Title, movie.SortTitle, movie.Overview));
    }

    [Fact]
    public async Task ARefreshDuringTheSync_IsNotUndoneByItsOlderList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()));
        await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        // Radarr deletes movie 2 and its webhook is refreshed while the sync holds a list that still has it
        var radarr = clients.Radarr["radarr"];
        var syncLock = new SyncLock();
        radarr.AfterList = async () =>
        {
            radarr.Movies = RealMovies().Where(movie => movie.Id != 2).ToList();
            await SyncHarness.RefreshAsync(database, clients, [Radarr], "radarr", 2, cancellationToken, syncLock);
        };
        var report = await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken, syncLock)
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        Assert.Equal(0, report.Instances[0].Added);
        await using var context = database.CreateAkiraContext();
        Assert.Equal(["movie:tmdb:170"], await context.MediaItems.Select(i => i.IdentityKey).ToListAsync(cancellationToken));
    }

    [Fact]
    public async Task RemovedFromOneInstance_KeepsItemWithTheOtherVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()), ("radarr-4k", Upgraded4K(RealMovies())));
        await SyncHarness.SyncAsync(database, clients, [Radarr, Radarr4K], cancellationToken);
        var (itemId, _) = await GetIdsAsync(database, "movie:tmdb:170", cancellationToken);

        clients.Radarr["radarr-4k"].Movies = [];
        var report = await SyncHarness.SyncAsync(database, clients, [Radarr, Radarr4K], cancellationToken);

        Assert.Equal((2, 0), (report.Instances[1].Unlinked, report.RemovedItems));
        await using var context = database.CreateAkiraContext();
        var movie = await context.MediaItems.Include(i => i.Files).SingleAsync(i => i.Id == itemId, cancellationToken);
        Assert.Equal("radarr", Assert.Single(movie.Files).InstanceKey);
    }

    [Fact]
    public async Task FailedInstance_KeepsItsVersions_WhileTheOtherRemovesTheMovie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()), ("radarr-4k", Upgraded4K(RealMovies())));
        await SyncHarness.SyncAsync(database, clients, [Radarr, Radarr4K], cancellationToken);

        clients.Radarr["radarr"].Movies = [];
        clients.Radarr["radarr-4k"].Failure = new UpstreamException("radarr-4k", UpstreamFailure.Unreachable, "unreachable");
        var report = await SyncHarness.SyncAsync(database, clients, [Radarr, Radarr4K], cancellationToken);

        Assert.Equal(0, report.RemovedItems);
        await using var context = database.CreateAkiraContext();
        Assert.Equal(2, await context.MediaItems.CountAsync(cancellationToken));
        Assert.All(await context.MediaFiles.ToListAsync(cancellationToken), file => Assert.Equal("radarr-4k", file.InstanceKey));
    }

    [Fact]
    public async Task InstanceTakenOutOfConfiguration_DropsItsVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        var clients = Clients(("radarr", RealMovies()), ("radarr-4k", Upgraded4K(RealMovies())));
        await SyncHarness.SyncAsync(database, clients, [Radarr, Radarr4K], cancellationToken);

        var report = await SyncHarness.SyncAsync(database, clients, [Radarr], cancellationToken);

        Assert.Equal(0, report.RemovedItems);
        await using var context = database.CreateAkiraContext();
        Assert.Equal(["radarr"], (await context.MediaFiles.Select(file => file.InstanceKey).ToListAsync(cancellationToken)).Distinct());
        Assert.Equal(["radarr"], (await context.ArrLinks.Select(link => link.InstanceKey).ToListAsync(cancellationToken)).Distinct());
        Assert.Equal(["radarr"], (await context.MediaImages.Select(image => image.InstanceKey).ToListAsync(cancellationToken)).Distinct());
    }

    [Fact]
    public async Task NoInstancesConfigured_LeavesLibraryAlone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await CreateMigratedDatabaseAsync(cancellationToken);
        await SyncHarness.SyncAsync(database, Clients(("radarr", RealMovies())), [Radarr], cancellationToken);

        var report = await SyncHarness.SyncAsync(database, new FakeArrClientFactory(), [], cancellationToken);

        Assert.Equal((0, 0), (report.Instances.Count, report.RemovedItems));
        await using var context = database.CreateAkiraContext();
        Assert.Equal(2, await context.MediaItems.CountAsync(cancellationToken));
    }

    private async Task<TestDatabase> CreateMigratedDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = await CreateDatabaseAsync(cancellationToken);
        database.Migrate();
        return database;
    }

    private static FakeArrClientFactory Clients(params (string Key, List<RadarrMovie> Movies)[] instances)
    {
        var clients = new FakeArrClientFactory();
        foreach (var (key, movies) in instances)
        {
            clients.Radarr[key] = new FakeRadarrClient { Movies = movies };
        }

        return clients;
    }

    // The same movies as a 4K Radarr has them: its own ids, files and a later added date
    private static List<RadarrMovie> Upgraded4K(List<RadarrMovie> movies) => movies
        .Select(movie => movie with
        {
            Id = movie.Id + 100,
            Added = movie.Added?.AddDays(30),
            MovieFile = new RadarrMovieFile(movie.MovieFile!.Id + 100,
                movie.MovieFile.Path!.Replace("Bluray-1080p.mp4", "Remux-2160p.mkv").Replace("Bluray-1080p.mkv", "Remux-2160p.mkv"),
                50_000_000_000, movie.Added?.AddDays(30))
        })
        .ToList();

    private static async Task<(Guid ItemId, Guid? FileId)> GetIdsAsync(TestDatabase database, string key, CancellationToken cancellationToken)
    {
        await using var context = database.CreateAkiraContext();
        var item = await context.MediaItems.Include(i => i.Files).SingleAsync(i => i.IdentityKey == key, cancellationToken);
        return (item.Id, item.Files.FirstOrDefault()?.Id);
    }

    private static List<RadarrMovie> RealMovies() => Fixtures.Load<List<RadarrMovie>>("radarr-movies.json");
}
