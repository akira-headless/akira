namespace Akira.Infrastructure.Processes;

public sealed class ProcessRunException : Exception
{
    public ProcessRunFailure Failure { get; }

    public ProcessRunException(ProcessRunFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }
}
