namespace Akira.Infrastructure.Interfaces.Probing;

public interface IProbeService
{
    /// <summary>
    /// Probes every file that is new, changed or probed by an older version, batch by batch.
    /// </summary>
    Task<ProbeRunResult> ProbePendingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Probes those of the files that wait for their first probe, such as the versions of an item about to be played.
    /// </summary>
    Task<ProbeRunResult> ProbeFilesAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken);

    /// <summary>
    /// Checks probed files for changes on disk and new or removed subtitles, and queues missing and failed files again.
    /// </summary>
    Task<RescanResult> RescanAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the subtitle files next to one version again, right after one was added, and returns its external tracks.
    /// </summary>
    Task<IReadOnlyList<ExternalTrack>> RescanSubtitlesAsync(Guid fileId, CancellationToken cancellationToken);
}
