namespace Akira.Contracts.Subtitles;

/// <summary>
/// Defines the states of a translation job.
/// </summary>
public enum TranslationState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}
