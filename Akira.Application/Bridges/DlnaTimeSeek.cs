using System.Globalization;

namespace Akira.Server.Bridges;

/// <summary>
/// Reads and writes TimeSeekRange.dlna.org, with which a renderer asks a stream without byte ranges to start later.
/// </summary>
public static class DlnaTimeSeek
{
    public const string Header = "TimeSeekRange.dlna.org";

    /// <summary>
    /// Returns the start in seconds of npt=123.45- or npt=0:02:03.450-, or null when the header is not there or not a
    /// time.
    /// </summary>
    public static double? Start(string? header, long durationMs)
    {
        var value = (header ?? "").Trim();
        if (!value.StartsWith("npt=", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var start = value[4..].Split('-')[0].Trim();
        double seconds;
        if (start.Contains(':'))
        {
            var parts = start.Split(':');
            if (parts.Length != 3
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
                || !double.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rest))
            {
                return null;
            }

            seconds = hours * 3600.0 + minutes * 60 + rest;
        }
        else if (!double.TryParse(start, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds))
        {
            return null;
        }

        return Math.Clamp(seconds, 0, Math.Max(0, durationMs / 1000.0 - 1));
    }

    /// <summary>
    /// Returns what the answer says the stream holds: from the start to the end, of the whole duration.
    /// </summary>
    public static string Range(double startSeconds, long durationMs)
    {
        var duration = durationMs / 1000.0;
        return string.Create(CultureInfo.InvariantCulture, $"npt={startSeconds:0.000}-{duration:0.000}/{duration:0.000}");
    }
}
