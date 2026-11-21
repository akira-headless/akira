namespace Akira.Infrastructure.Processes;

/// <summary>
/// Represents how a program ended. Output is what it printed up to the limit of the run, Error the last part of what it
/// wrote as errors. OutputTruncated says it printed more than was kept.
/// </summary>
public sealed record ProcessResult(int ExitCode, byte[] Output, string Error, bool OutputTruncated);
