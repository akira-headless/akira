namespace Akira.Infrastructure.Probing;

/// <summary>
/// Holds whether ffprobe could be started at the last check, shared by the probe worker and the health endpoint.
/// </summary>
public sealed class FfprobeStatus
{
    private volatile Snapshot _current = new(null, null, null);

    /// <summary>
    /// Represents the outcome of the last ffprobe check. IsAvailable is null until the first check has run.
    /// </summary>
    public sealed record Snapshot(bool? IsAvailable, string? Version, string? Error);

    public Snapshot Current => _current;

    public void Available(string version) => _current = new Snapshot(true, version, null);

    public void Unavailable(string error) => _current = new Snapshot(false, null, error);
}
