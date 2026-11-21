using Akira.Infrastructure.Integrations;
using Akira.Infrastructure.Integrations.Radarr;
using Akira.Infrastructure.Interfaces.Integrations;

namespace Akira.Testing;

public sealed class FakeRadarrClient : IRadarrClient
{
    public List<RadarrMovie> Movies { get; set; } = [];
    public UpstreamException? Failure { get; set; }

    // Runs once, after the list was read and before it is handed over: what happens in Radarr while a sync has its answer
    public Func<Task>? AfterList { get; set; }

    // Runs before every change asked of this Radarr, with its description
    public Func<string, Task>? BeforeCall { get; set; }

    public async Task<IReadOnlyList<RadarrMovie>> GetMoviesAsync(CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        var movies = Movies.ToList();
        if (AfterList is { } afterList)
        {
            AfterList = null;
            await afterList();
        }

        return movies;
    }

    public Task<RadarrMovie> GetMovieAsync(int movieId, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException<RadarrMovie>(Failure);
        }

        return Movies.FirstOrDefault(movie => movie.Id == movieId) is { } movie
            ? Task.FromResult(movie)
            : Task.FromException<RadarrMovie>(new UpstreamException("Radarr", UpstreamFailure.NotFound, $"Radarr has no movie {movieId}."));
    }

    public Task<ImageData> GetMediaCoverAsync(int movieId, string fileName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    // Every change asked of this Radarr, as "delete-file 5", "monitored 1 false", "search 1"
    public List<string> Calls { get; } = [];

    // Makes the calls whose description starts with the key fail
    public Dictionary<string, Exception> FailOn { get; } = [];

    public Task DeleteMovieFileAsync(int movieFileId, CancellationToken cancellationToken) =>
        Record($"delete-file {movieFileId}", () =>
        {
            foreach (var movie in Movies.Where(movie => movie.MovieFile?.Id == movieFileId).ToList())
            {
                Movies[Movies.IndexOf(movie)] = movie with { HasFile = false, MovieFile = null };
            }
        });

    public Task SetMonitoredAsync(IReadOnlyList<int> movieIds, bool monitored, CancellationToken cancellationToken) =>
        Record($"monitored {string.Join(",", movieIds)} {monitored.ToString().ToLowerInvariant()}");

    public Task SearchAsync(IReadOnlyList<int> movieIds, CancellationToken cancellationToken) =>
        Record($"search {string.Join(",", movieIds)}");

    private async Task Record(string call, Action? apply = null)
    {
        if (BeforeCall is not null)
        {
            await BeforeCall(call);
        }

        if (FailOn.FirstOrDefault(failure => call.StartsWith(failure.Key, StringComparison.Ordinal)).Value is { } exception)
        {
            throw exception;
        }

        Calls.Add(call);
        apply?.Invoke();
    }
}
