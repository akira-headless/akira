namespace Akira.Server.Services.Playback;

public interface IMarkerScanService
{
    /// <summary>
    /// Compares the audio of the next season that has an episode not compared yet. False when every season is done.
    /// </summary>
    Task<bool> ScanNextSeasonAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes the cached fingerprints of files the library no longer has.
    /// </summary>
    Task RemoveOrphansAsync(CancellationToken cancellationToken);
}
