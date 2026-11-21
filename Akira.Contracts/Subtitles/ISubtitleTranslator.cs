namespace Akira.Contracts.Subtitles;

/// <summary>
/// Defines a provider that translates a subtitle file into another language.
/// </summary>
public interface ISubtitleTranslator
{
    string Key { get; }
    string Name { get; }
    bool IsConfigured { get; }

    // The subtitle formats it translates, as file extensions without the dot
    IReadOnlySet<string> Formats { get; }

    /// <summary>
    /// Starts translating a subtitle file on the share and returns the translator's job id.
    /// </summary>
    Task<string> StartAsync(TranslationJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves how far a job is, with the translated file's path on this server once it is done.
    /// </summary>
    Task<TranslationProgress> GetAsync(string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the translator to stop a job.
    /// </summary>
    Task CancelAsync(string jobId, CancellationToken cancellationToken);
}
