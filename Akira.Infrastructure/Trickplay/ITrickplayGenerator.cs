using Akira.Core.Playback;

namespace Akira.Infrastructure.Trickplay;

public interface ITrickplayGenerator
{
    /// <summary>
    /// Writes the thumbnail sheets of a video to a folder, replacing what was there, and returns their layout.
    /// </summary>
    Task<TrickplayLayout> GenerateAsync(string videoPath, int? videoWidth, int? videoHeight, long durationMs, string folder,
        CancellationToken cancellationToken);
}
