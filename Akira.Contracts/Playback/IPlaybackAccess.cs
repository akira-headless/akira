using Akira.Contracts.Accounts;

namespace Akira.Contracts.Playback;

/// <summary>
/// Defines playback for plugins: planning a session, starting it, reporting on it, and the skip markers and seek
/// thumbnails of a file. A report answers false when the session is not the caller's or has ended.
/// </summary>
public interface IPlaybackAccess
{
    /// <summary>
    /// Plans a playback and makes its session without starting it, or returns null when the item does not exist or has
    /// nothing to play. A client may ask for a plan and never play, so nothing shows the user as watching yet.
    /// </summary>
    Task<PlannedPlayback?> PlanAsync(Caller caller, PlaybackRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a planned session once the client really plays. A first progress report starts it as well.
    /// </summary>
    Task<bool> StartAsync(Caller caller, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Reports where the client is in a playback session and whether it is paused.
    /// </summary>
    Task<bool> ProgressAsync(Caller caller, Guid sessionId, long positionMs, bool paused, CancellationToken cancellationToken);

    /// <summary>
    /// Records the tracks the client switched to: the session plays them from now on and, when remember is set, the server
    /// keeps them for the series or movie. A track left null and subtitlesOff false stays as it was. False as well when a
    /// stream id is not a track of the session's file.
    /// </summary>
    Task<bool> TracksAsync(
        Caller caller,
        Guid sessionId,
        string? audioStreamId,
        string? subtitleStreamId,
        bool subtitlesOff,
        bool remember,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends a playback session at a position, which updates the user's watch state. Without a position it ends where
    /// the last progress report was.
    /// </summary>
    Task<bool> StopAsync(Caller caller, Guid sessionId, long? positionMs, CancellationToken cancellationToken);

    /// <summary>
    /// Ends a playback session nobody knows the position of, or one that never played, and leaves the user's watch
    /// state as it is.
    /// </summary>
    Task<bool> EndAsync(Caller caller, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Says for each file what its progressive stream (IMediaAccess.ProgressiveUrl) sends this caller: the file as it is,
    /// with byte ranges, when it already is what every renderer plays and nothing has to be burned in, otherwise a
    /// transcode with a time seek. Makes no session. Files that do not exist are left out.
    /// </summary>
    Task<IReadOnlyList<ProgressiveStream>> ProgressiveAsync(Caller caller, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the parts of a file a player can offer to skip, from its chapters and the audio scan.
    /// </summary>
    Task<IReadOnlyList<PlaybackMarker>> MarkersAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the seek thumbnails of a file, or null until they are made.
    /// </summary>
    Task<PlaybackTrickplay?> TrickplayAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one sheet of seek thumbnails of a file as JPEG, or null when there is none.
    /// </summary>
    Task<byte[]?> TrickplaySheetAsync(Guid fileId, int sheet, CancellationToken cancellationToken);
}
