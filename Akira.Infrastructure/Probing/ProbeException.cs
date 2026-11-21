namespace Akira.Infrastructure.Probing;

public sealed class ProbeException : Exception
{
    public ProbeFailure Failure { get; }

    public ProbeException(ProbeFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }
}
