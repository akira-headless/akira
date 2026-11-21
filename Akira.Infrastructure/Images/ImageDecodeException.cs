namespace Akira.Infrastructure.Images;

public sealed class ImageDecodeException : Exception
{
    public ImageDecodeFailure Failure { get; }

    public ImageDecodeException(ImageDecodeFailure failure, string message, Exception? innerException = null) : base(message, innerException)
    {
        Failure = failure;
    }
}
