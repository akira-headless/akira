using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Probing;
using Akira.Testing;
using Xunit;

namespace Akira.Infrastructure.Tests.Probing;

// Needs ffprobe, which the integration job installs
public abstract class ProbeServiceTests
{
    private const string VideoName = "Synthetic Movie (2026).mkv";

    protected abstract Task<TestDatabase> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task Probe_StoresTracksChaptersAndExternalSubtitles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = await SetUpAsync(cancellationToken);
        setup.AddSubtitle("Synthetic Movie (2026).nld.srt");
        setup.AddSubtitle("Synthetic Movie (2026).en.forced.srt");

        var result = await setup.ProbePendingAsync(cancellationToken);

        Assert.Equal((1, 0, 0), (result.Probed, result.Missing, result.Failed));
        var file = await setup.LoadFileAsync(cancellationToken);
        Assert.Equal(ProbeStatus.Probed, file.ProbeStatus);
        Assert.Equal(FfprobeParser.Version, file.ProbeVersion);
        Assert.Equal(1021, file.DurationMs);
        Assert.Equal(new FileInfo(setup.VideoPath).Length, file.ProbedSizeBytes);
        Assert.Equal(["Opening", "Part A"], file.Chapters.OrderBy(c => c.SortOrder).Select(c => c.Title!));

        var streams = file.Streams.OrderBy(s => s.SortOrder).ToList();
        Assert.Equal(10, streams.Count);
        Assert.Equal(HdrFormat.Hdr10, streams[0].HdrFormat);
        var externals = streams.Where(s => s.IsExternal).ToList();
        Assert.Equal([("eng", true), ("dut", false)], externals.Select(s => (s.Language!, s.IsForced)));
        Assert.All(externals, s => Assert.Equal(12, s.EventCount));
    }

    [Fact]
    public async Task ProbingAgain_KeepsTheSameTrackIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = await SetUpAsync(cancellationToken);
        setup.AddSubtitle("Synthetic Movie (2026).nld.srt");
        await setup.ProbePendingAsync(cancellationToken);
        var before = (await setup.LoadFileAsync(cancellationToken)).Streams.Select(s => s.Id).Order().ToList();

        await setup.SetStatusAsync(ProbeStatus.Pending, cancellationToken);
        await setup.ProbePendingAsync(cancellationToken);

        Assert.Equal(before, (await setup.LoadFileAsync(cancellationToken)).Streams.Select(s => s.Id).Order());
    }

    [Fact]
    public async Task Rescan_PicksUpNewAndRemovedSubtitles_WithoutTouchingTheRest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = await SetUpAsync(cancellationToken);
        var dutch = setup.AddSubtitle("Synthetic Movie (2026).nld.srt");
        await setup.ProbePendingAsync(cancellationToken);
        var embedded = (await setup.LoadFileAsync(cancellationToken)).Streams.Where(s => !s.IsExternal).Select(s => s.Id).Order().ToList();

        File.Delete(dutch);
        setup.AddSubtitle("Synthetic Movie (2026).en.lingarr.srt");
        var rescan = await setup.RescanAsync(cancellationToken);

        Assert.Equal((0, 1, 1), (rescan.Requeued, rescan.SubtitlesAdded, rescan.SubtitlesRemoved));
        var file = await setup.LoadFileAsync(cancellationToken);
        var external = Assert.Single(file.Streams, s => s.IsExternal);
        Assert.Equal(("eng", "lingarr"), (external.Language!, external.Title!));
        Assert.Equal(embedded, file.Streams.Where(s => !s.IsExternal).Select(s => s.Id).Order());
    }

    [Fact]
    public async Task Rescan_OfAnUnchangedLibrary_ChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = await SetUpAsync(cancellationToken);
        await setup.ProbePendingAsync(cancellationToken);

        var rescan = await setup.RescanAsync(cancellationToken);

        Assert.Equal((0, 0, 0), (rescan.Requeued, rescan.SubtitlesAdded, rescan.SubtitlesRemoved));
    }

    [Fact]
    public async Task Rescan_FileChangedInPlace_IsProbedAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = await SetUpAsync(cancellationToken);
        await setup.ProbePendingAsync(cancellationToken);

        await using (var stream = File.Open(setup.VideoPath, FileMode.Append))
        {
            stream.WriteByte(0);
        }

        var rescan = await setup.RescanAsync(cancellationToken);
        var result = await setup.ProbePendingAsync(cancellationToken);

        Assert.Equal((1, 1), (rescan.Requeued, result.Probed));
        Assert.Equal(new FileInfo(setup.VideoPath).Length, (await setup.LoadFileAsync(cancellationToken)).ProbedSizeBytes);
    }

    [Fact]
    public async Task MissingFile_IsMissingWithAPathMappingHint_AndRetriedOnRescan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = await SetUpAsync(cancellationToken, videoExists: false);

        var result = await setup.ProbePendingAsync(cancellationToken);

        Assert.Equal(1, result.Missing);
        var file = await setup.LoadFileAsync(cancellationToken);
        Assert.Equal(ProbeStatus.Missing, file.ProbeStatus);
        Assert.Contains("Check the path mappings of instance radarr", file.ProbeError);

        setup.CreateVideo();
        var rescan = await setup.RescanAsync(cancellationToken);
        var retried = await setup.ProbePendingAsync(cancellationToken);

        Assert.Equal((1, 1), (rescan.Requeued, retried.Probed));
    }

    [Fact]
    public async Task BrokenFile_IsFailedWithFfprobesMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = await SetUpAsync(cancellationToken, videoExists: false);
        await File.WriteAllTextAsync(setup.VideoPath, "not a video", cancellationToken);

        var result = await setup.ProbePendingAsync(cancellationToken);

        Assert.Equal(1, result.Failed);
        var file = await setup.LoadFileAsync(cancellationToken);
        Assert.Equal(ProbeStatus.Failed, file.ProbeStatus);
        Assert.Contains("ffprobe exited with", file.ProbeError);
        Assert.Empty(file.Streams);

        // Not again on every rescan: after half an hour, or at once when the file changed
        var soon = await setup.RescanAsync(cancellationToken);
        setup.Time.Advance(TimeSpan.FromMinutes(31));
        var later = await setup.RescanAsync(cancellationToken);
        await setup.ProbePendingAsync(cancellationToken);
        await File.AppendAllTextAsync(setup.VideoPath, ", still", cancellationToken);
        var changed = await setup.RescanAsync(cancellationToken);

        Assert.Equal((0, 1, 1), (soon.Requeued, later.Requeued, changed.Requeued));
    }

    [Fact]
    public async Task Rescan_OfAFolderWithSeveralVideos_GivesEachItsOwnSubtitles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var setup = await SetUpAsync(cancellationToken);
        var other = await setup.AddVideoAsync("Synthetic Movie (2026) - Part 2.mkv", cancellationToken);
        await setup.ProbePendingAsync(cancellationToken);

        setup.AddSubtitle("Synthetic Movie (2026).nld.srt");
        setup.AddSubtitle("Synthetic Movie (2026) - Part 2.en.srt");
        var rescan = await setup.RescanAsync(cancellationToken);

        Assert.Equal(2, rescan.SubtitlesAdded);
        Assert.Equal("dut", Assert.Single((await setup.LoadFileAsync(cancellationToken)).Streams, s => s.IsExternal).Language);
        Assert.Equal("eng", Assert.Single((await setup.LoadFileAsync(other, cancellationToken)).Streams, s => s.IsExternal).Language);
    }

    private async Task<ProbeSetup> SetUpAsync(CancellationToken cancellationToken, bool videoExists = true)
    {
        var database = await CreateDatabaseAsync(cancellationToken);
        database.Migrate();
        var setup = new ProbeSetup(database);
        if (videoExists)
        {
            setup.CreateVideo();
        }

        await setup.AddFileAsync(cancellationToken);
        return setup;
    }

    private sealed class ProbeSetup(TestDatabase database) : IAsyncDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("akira_probe_").FullName;
        private Guid _fileId;

        public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

        public string VideoPath => Path.Combine(_directory, VideoName);

        public void CreateVideo() =>
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "media", "synthetic.mkv"), VideoPath, overwrite: true);

        public string AddSubtitle(string fileName)
        {
            var path = Path.Combine(_directory, fileName);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "media", "sample.srt"), path, overwrite: true);
            return path;
        }

        public async Task AddFileAsync(CancellationToken cancellationToken) => _fileId = await AddFileAsync(VideoPath, 1, cancellationToken);

        public Task<Guid> AddVideoAsync(string fileName, CancellationToken cancellationToken)
        {
            var path = Path.Combine(_directory, fileName);
            File.Copy(VideoPath, path);
            return AddFileAsync(path, 2, cancellationToken);
        }

        private async Task<Guid> AddFileAsync(string path, int number, CancellationToken cancellationToken)
        {
            await using var context = database.CreateAkiraContext();
            var item = new MediaItem
            {
                Type = MediaItemType.Movie,
                IdentityKey = $"movie:tmdb:{number}",
                Title = "Synthetic Movie",
                SortTitle = "synthetic movie"
            };
            var file = new MediaFile
            {
                MediaItemId = item.Id,
                InstanceKey = "radarr",
                Path = path,
                SizeBytes = 1,
                ArrFileId = number
            };
            context.MediaItems.Add(item);
            context.ArrLinks.Add(new ArrLink { MediaItemId = item.Id, InstanceKey = "radarr", ArrId = number });
            context.MediaFiles.Add(file);
            await context.SaveChangesAsync(cancellationToken);
            return file.Id;
        }

        public async Task<Interfaces.Probing.ProbeRunResult> ProbePendingAsync(CancellationToken cancellationToken)
        {
            await using var context = database.CreateAkiraContext();
            return await Service(context).ProbePendingAsync(cancellationToken);
        }

        public async Task<Interfaces.Probing.RescanResult> RescanAsync(CancellationToken cancellationToken)
        {
            await using var context = database.CreateAkiraContext();
            return await Service(context).RescanAsync(cancellationToken);
        }

        public async Task SetStatusAsync(ProbeStatus status, CancellationToken cancellationToken)
        {
            await using var context = database.CreateAkiraContext();
            await context.MediaFiles.Where(file => file.Id == _fileId)
                .ExecuteUpdateAsync(file => file.SetProperty(f => f.ProbeStatus, status), cancellationToken);
        }

        public Task<MediaFile> LoadFileAsync(CancellationToken cancellationToken) => LoadFileAsync(_fileId, cancellationToken);

        public async Task<MediaFile> LoadFileAsync(Guid id, CancellationToken cancellationToken)
        {
            await using var context = database.CreateAkiraContext();
            return await context.MediaFiles.Include(file => file.Streams).Include(file => file.Chapters)
                .SingleAsync(file => file.Id == id, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await database.DisposeAsync();
            Directory.Delete(_directory, recursive: true);
        }

        private ProbeService Service(Data.AkiraDbContext context) =>
            new(context,
                new FfprobeRunner(Options.Create(new ProbingOptions { FfprobePath = Ffprobe.Path })),
                Options.Create(new ProbingOptions()),
                Time,
                NullLogger<ProbeService>.Instance);
    }
}
