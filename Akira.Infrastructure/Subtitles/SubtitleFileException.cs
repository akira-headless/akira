namespace Akira.Infrastructure.Subtitles;

public sealed class SubtitleFileException : Exception
{
    public SubtitleFileException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}
