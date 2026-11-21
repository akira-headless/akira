namespace Akira.Infrastructure.Images;

public interface IImageDecoder
{
    /// <summary>
    /// Shrinks an image to Size by Size pixels and returns them as RGB bytes, row by row.
    /// </summary>
    Task<byte[]> DecodeAsync(byte[] image, CancellationToken cancellationToken);
}
