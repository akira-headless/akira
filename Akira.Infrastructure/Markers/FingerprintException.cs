namespace Akira.Infrastructure.Markers;

public sealed class FingerprintException : Exception
{
    public FingerprintFailure Failure { get; }

    public FingerprintException(FingerprintFailure failure, string message, Exception? innerException = null) : base(message, innerException)
    {
        Failure = failure;
    }
}
