namespace Akira.Contracts.Subtitles;

/// <summary>
/// Represents a failed provider call. Transient means trying again later may work, such as after a timeout or an
/// unreachable host. Anything else ends the request with the message, which users see.
/// </summary>
public sealed class ProviderException : Exception
{
    public bool Transient { get; }

    public ProviderException(string message, bool transient = false, Exception? innerException = null)
        : base(message, innerException)
    {
        Transient = transient;
    }
}
