using System.Globalization;
using System.Text;

namespace Akira.Core.Playback;

/// <summary>
/// Writes the HLS playlists of a transcoded version. The media playlist lists every segment up front, so a player
/// can seek anywhere, and a segment always starts where it says.
/// </summary>
public static class HlsPlaylists
{
    public static string Master(string mediaUrl, long bandwidth, int? width, int? height)
    {
        var stream = new StringBuilder("#EXT-X-STREAM-INF:BANDWIDTH=").Append(bandwidth.ToString(CultureInfo.InvariantCulture));
        if (width is > 0 && height is > 0)
        {
            stream.Append(",RESOLUTION=").Append(width.Value.ToString(CultureInfo.InvariantCulture))
                .Append('x').Append(height.Value.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join('\n', "#EXTM3U", "#EXT-X-VERSION:3", stream.ToString(), mediaUrl) + "\n";
    }

    public static string Media(HlsSegments segments, Func<int, string> segmentUrl)
    {
        var targetDuration = (int)Math.Ceiling(segments.Longest);
        var builder = new StringBuilder()
            .Append("#EXTM3U\n#EXT-X-VERSION:3\n")
            .Append("#EXT-X-TARGETDURATION:").Append(targetDuration.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n");

        for (var index = 0; index < segments.Count; index++)
        {
            var length = segments.End(index) - segments.Start(index);
            builder.Append("#EXTINF:").Append(length.ToString("0.000000", CultureInfo.InvariantCulture)).Append(",\n")
                .Append(segmentUrl(index)).Append('\n');
        }

        return builder.Append("#EXT-X-ENDLIST\n").ToString();
    }
}
