using Akira.Infrastructure.Probing;

namespace Akira.Infrastructure.Interfaces.Probing;

public interface IFfprobeRunner
{
    /// <summary>
    /// Retrieves the ffprobe version, which fails with NotInstalled when ffprobe cannot be started.
    /// </summary>
    Task<string> GetVersionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the streams, format and chapters of a media file without decoding it.
    /// </summary>
    Task<FfprobeOutput> ProbeAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a subtitle file and counts its events.
    /// </summary>
    Task<FfprobeOutput> ProbeSubtitleAsync(string path, CancellationToken cancellationToken);
}
