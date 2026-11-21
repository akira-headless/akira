namespace Akira.Infrastructure.Processes;

/// <summary>
/// Represents one run of a program that is waited for. Output past MaxOutputBytes is read and dropped, 0 keeps none.
/// BelowNormalPriority is for work nobody waits on.
/// </summary>
public sealed record ProcessRun(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    int MaxOutputBytes = 0,
    bool BelowNormalPriority = false);
