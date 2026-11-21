namespace Akira.Infrastructure.Trickplay;

public sealed class TrickplayException : Exception
{
    public TrickplayFailure Failure { get; }

    public TrickplayException(TrickplayFailure failure, string message, Exception? innerException = null) : base(message, innerException)
    {
        Failure = failure;
    }
}
