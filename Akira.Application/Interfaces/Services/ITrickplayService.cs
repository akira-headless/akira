using Akira.Server.Models.Playback;

namespace Akira.Server.Interfaces.Services;

public interface ITrickplayService
{
    /// <summary>
    /// Retrieves the thumbnail layout of a version with the urls of its sheets, or null when none were made yet.
    /// </summary>
    Task<TrickplayModel?> GetAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the WebVTT thumbnails track of a version, or null when none were made yet.
    /// </summary>
    Task<string?> GetVttAsync(Guid fileId, CancellationToken cancellationToken);

    Task<string?> GetSheetPathAsync(Guid fileId, int sheet, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the thumbnails of the next file that has none or outdated ones. False when every file is done.
    /// </summary>
    Task<bool> GenerateNextAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes the thumbnail folders of files the library no longer has.
    /// </summary>
    Task RemoveOrphansAsync(CancellationToken cancellationToken);
}
