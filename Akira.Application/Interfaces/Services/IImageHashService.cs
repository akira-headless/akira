namespace Akira.Server.Interfaces.Services;

public interface IImageHashService
{
    /// <summary>
    /// Makes the blur hashes of the next images that have none, past the first skip of them, which this round could not fetch.
    /// </summary>
    Task<ImageHashBatch> HashNextAsync(int skip, CancellationToken cancellationToken);
}
