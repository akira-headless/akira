using Akira.Infrastructure.Integrations;

namespace Akira.Infrastructure.Interfaces.Integrations;

public interface IArtworkClient
{
    /// <summary>
    /// Retrieves an image from an allowed artwork host such as TMDB or TheTVDB.
    /// </summary>
    Task<ImageData> GetAsync(string url, CancellationToken cancellationToken);
}
