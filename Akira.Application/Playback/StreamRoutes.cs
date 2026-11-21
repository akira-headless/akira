using System.Globalization;
using Akira.Server.Endpoints;

namespace Akira.Server.Playback;

/// <summary>
/// Builds the urls of streamed files, matching the routes in FileController and PlaybackController.
/// </summary>
public static class StreamRoutes
{
    public const string FileStream = "{id:guid}/stream";
    public const string FileDownload = "{id:guid}/download";
    public const string Subtitle = "{id:guid}/subtitles/{streamId}";
    public const string SubtitleWebVtt = "{id:guid}/subtitles/{streamId}/vtt";

    public const string Trickplay = "{id:guid}/trickplay";
    public const string TrickplaySheet = "{id:guid}/trickplay/{sheet:int}.jpg";
    public const string TrickplayVtt = "{id:guid}/trickplay/thumbnails.vtt";

    public static string TrickplayUrl(Guid fileId) => $"{ItemRoutes.ApiPrefix}{ItemRoutes.Files}/{fileId}/trickplay";

    public static string TrickplaySheetUrl(Guid fileId, int sheet) =>
        $"{ItemRoutes.ApiPrefix}{ItemRoutes.Files}/{fileId}/trickplay/{sheet.ToString(CultureInfo.InvariantCulture)}.jpg";

    public static string TrickplayVttUrl(Guid fileId) => $"{ItemRoutes.ApiPrefix}{ItemRoutes.Files}/{fileId}/trickplay/thumbnails.vtt";

    public const string Sessions = "/sessions";
    public const string HlsMaster = "{id:guid}/hls/master.m3u8";
    public const string HlsMedia = "{id:guid}/hls/main.m3u8";
    public const string HlsSegment = "{id:guid}/hls/{segment:int}.ts";

    // Relative to the playlists, so a player resolves them against wherever it fetched the playlist
    public const string HlsMediaName = "main.m3u8";

    // The version changes with the tracks: a segment may be cached, and after a track change the browser would
    // play the copy it has of the old track
    public static string HlsSegmentName(int index, string version) =>
        index.ToString(CultureInfo.InvariantCulture) + ".ts?v=" + version;

    public static string HlsMasterUrl(Guid sessionId) => $"{ItemRoutes.ApiPrefix}{Sessions}/{sessionId}/hls/master.m3u8";

    public static string SubtitleWebVttUrl(Guid fileId, string streamId) =>
        $"{ItemRoutes.ApiPrefix}{ItemRoutes.Files}/{fileId}/subtitles/{streamId}/vtt";

    public static string FileStreamUrl(Guid fileId) => $"{ItemRoutes.ApiPrefix}{ItemRoutes.Files}/{fileId}/stream";

    public static string SubtitleUrl(Guid fileId, string streamId) =>
        $"{ItemRoutes.ApiPrefix}{ItemRoutes.Files}/{fileId}/subtitles/{streamId}";
}
