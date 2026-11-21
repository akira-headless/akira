namespace Akira.Contracts.Subtitles;

/// <summary>
/// Defines a provider that searches and downloads existing subtitles.
/// </summary>
public interface ISubtitleSource
{
    string Key { get; }
    string Name { get; }
    bool IsConfigured { get; }

    /// <summary>
    /// Searches for existing subtitles of a movie or episode in one language, best match first.
    /// </summary>
    Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleSearch search, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads a subtitle a search found.
    /// </summary>
    Task<SubtitleDownload> DownloadAsync(string reference, CancellationToken cancellationToken);
}
