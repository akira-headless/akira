namespace Akira.Server.Interfaces.Services;

public interface ISubtitleRequestProcessor
{
    /// <summary>
    /// Moves every open request one step: downloads, starts or follows translations. Returns how many were open.
    /// </summary>
    Task<int> ProcessAsync(CancellationToken cancellationToken);
}
