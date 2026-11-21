using Akira.Contracts.Accounts;
using Akira.Contracts.Library;

namespace Akira.Contracts.Media;

/// <summary>
/// Makes signed urls, so a client that cannot send a credential with a request still streams. A url is a path on this server
/// for one file, subtitle or image, never holds the credential, and works for at least its lifetime while the caller's
/// credential exists.
/// </summary>
public interface IMediaAccess
{
    /// <summary>
    /// Returns a url to the file itself, with range requests, ending in the name for players that look at the extension.
    /// </summary>
    string FileUrl(Caller caller, Guid fileId, string fileName, TimeSpan lifetime);

    /// <summary>
    /// Returns a url to a text subtitle of a file, embedded or external, as WebVTT.
    /// </summary>
    string SubtitleUrl(Caller caller, Guid fileId, string streamId, TimeSpan lifetime);

    /// <summary>
    /// Returns a url to an image of an item. It stays the same for about its lifetime, so a client's image cache keeps
    /// what it loaded.
    /// </summary>
    string ArtworkUrl(Caller caller, Guid itemId, ArtworkKind kind, ArtworkSize size, TimeSpan lifetime);

    /// <summary>
    /// Returns the master.m3u8 url of a transcoding playback session, from which the whole stream plays because the
    /// playlists name their parts relatively.
    /// </summary>
    string HlsUrl(Caller caller, Guid sessionId, TimeSpan lifetime);

    /// <summary>
    /// Returns a url to a version as one MPEG-TS stream (H.264, AAC or AC-3, 1080p at most, SDR, the user's subtitle burned
    /// in) for players that take neither the file nor HLS: the file itself, with byte ranges, when it already is that,
    /// otherwise a transcode with a time seek (IPlaybackAccess.ProgressiveAsync says which). Opening it plans a playback
    /// with the caller's track rules.
    /// </summary>
    string ProgressiveUrl(Caller caller, Guid fileId, TimeSpan lifetime);
}
