namespace Akira.Infrastructure.Markers;

public interface IAudioFingerprinter
{
    /// <summary>
    /// Reads a stretch of one audio stream and returns its chromaprint fingerprint, a point per AudioMatch.SecondsPerPoint.
    /// </summary>
    Task<uint[]> FingerprintAsync(string path, int audioIndex, TimeSpan start, TimeSpan length, CancellationToken cancellationToken);
}
