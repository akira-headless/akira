using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Sonarr;
using Akira.Infrastructure.Interfaces.Integrations;

namespace Akira.Testing;

public sealed class FakeSonarrClient : ISonarrClient
{
    public List<SonarrSeries> Series { get; set; } = [];
    public Dictionary<int, List<SonarrEpisode>> Episodes { get; } = [];
    public Dictionary<int, List<SonarrEpisodeFile>> Files { get; } = [];
    public HashSet<int> FailingSeries { get; } = [];

    // Series whose episode list throws something no upstream call should, as a bug would
    public Dictionary<int, Exception> BrokenSeries { get; } = [];
    public UpstreamException? Failure { get; set; }

    // Runs once, after the list was read and before it is handed over: what happens in Sonarr while a sync has its answer
    public Func<Task>? AfterList { get; set; }

    public async Task<IReadOnlyList<SonarrSeries>> GetSeriesAsync(CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        var series = Series.ToList();
        if (AfterList is { } afterList)
        {
            AfterList = null;
            await afterList();
        }

        return series;
    }

    public Task<SonarrSeries> GetSeriesAsync(int seriesId, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException<SonarrSeries>(Failure);
        }

        return Series.FirstOrDefault(series => series.Id == seriesId) is { } series
            ? Task.FromResult(series)
            : Task.FromException<SonarrSeries>(new UpstreamException("Sonarr", UpstreamFailure.NotFound, $"Sonarr has no series {seriesId}."));
    }

    public Task<IReadOnlyList<SonarrEpisode>> GetEpisodesAsync(int seriesId, CancellationToken cancellationToken)
    {
        if (BrokenSeries.TryGetValue(seriesId, out var broken))
        {
            return Task.FromException<IReadOnlyList<SonarrEpisode>>(broken);
        }

        return FailingSeries.Contains(seriesId)
            ? Task.FromException<IReadOnlyList<SonarrEpisode>>(
                new UpstreamException("Sonarr", UpstreamFailure.Timeout, "Sonarr did not answer in time."))
            : Task.FromResult<IReadOnlyList<SonarrEpisode>>(Episodes.GetValueOrDefault(seriesId, []).ToList());
    }

    public Task<IReadOnlyList<SonarrEpisodeFile>> GetEpisodeFilesAsync(int seriesId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SonarrEpisodeFile>>(Files.GetValueOrDefault(seriesId, []).ToList());

    public Task<ImageData> GetMediaCoverAsync(int seriesId, string fileName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    // Every change asked of this Sonarr, as "delete-file 23", "episodes 256,258 false", "series 6 s2 false", "search 256"
    public List<string> Calls { get; } = [];

    public Dictionary<string, Exception> FailOn { get; } = [];

    public Task DeleteEpisodeFileAsync(int episodeFileId, CancellationToken cancellationToken) =>
        Record($"delete-file {episodeFileId}", () =>
        {
            foreach (var (seriesId, episodes) in Episodes)
            {
                Episodes[seriesId] = episodes
                    .Select(episode => episode.EpisodeFileId == episodeFileId ? episode with { HasFile = false, EpisodeFileId = null } : episode)
                    .ToList();
            }

            foreach (var files in Files.Values)
            {
                files.RemoveAll(file => file.Id == episodeFileId);
            }
        });

    public Task SetEpisodesMonitoredAsync(IReadOnlyList<int> episodeIds, bool monitored, CancellationToken cancellationToken) =>
        Record($"episodes {string.Join(",", episodeIds)} {monitored.ToString().ToLowerInvariant()}");

    public Task SetSeriesMonitoredAsync(int seriesId, int? seasonNumber, bool monitored, CancellationToken cancellationToken) =>
        Record($"series {seriesId}{(seasonNumber is null ? "" : $" s{seasonNumber}")} {monitored.ToString().ToLowerInvariant()}");

    public Task SearchEpisodesAsync(IReadOnlyList<int> episodeIds, CancellationToken cancellationToken) =>
        Record($"search {string.Join(",", episodeIds)}");

    private Task Record(string call, Action? apply = null)
    {
        if (FailOn.FirstOrDefault(failure => call.StartsWith(failure.Key, StringComparison.Ordinal)).Value is { } exception)
        {
            return Task.FromException(exception);
        }

        Calls.Add(call);
        apply?.Invoke();
        return Task.CompletedTask;
    }
}
