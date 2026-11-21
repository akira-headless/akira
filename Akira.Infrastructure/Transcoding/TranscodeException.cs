namespace Akira.Infrastructure.Transcoding;

public sealed class TranscodeException : Exception
{
    public TranscodeFailure Failure { get; }

    public TranscodeException(TranscodeFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }
}
