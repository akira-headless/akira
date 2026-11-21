using System.Globalization;
using System.Text;

namespace Akira.Core.Playback;

/// <summary>
/// Represents how trickplay thumbnails sit on their sheets. Width and Height are of one thumbnail, Columns and Rows of
/// a sheet, and Count is all thumbnails, one per IntervalMs.
/// </summary>
public sealed record TrickplayLayout(int IntervalMs, int Width, int Height, int Columns, int Rows, int Count)
{
    public int PerSheet => Columns * Rows;

    public int SheetCount => (Count + PerSheet - 1) / PerSheet;

    public static int CountFor(long durationMs, int intervalMs) => Math.Max(1, (int)Math.Ceiling(durationMs / (double)intervalMs));

    // The thumbnail height for a width, even like ffmpeg's scale=width:-2 makes it
    public static int HeightFor(int width, int? videoWidth, int? videoHeight)
    {
        if (videoWidth is not > 0 || videoHeight is not > 0)
        {
            return width * 9 / 16;
        }

        var height = width * (double)videoHeight.Value / videoWidth.Value;
        var evenHeight = (int)Math.Round(height / 2) * 2;
        return Math.Max(2, evenHeight);
    }

    public (int Sheet, int X, int Y) Position(int index)
    {
        var onSheet = index % PerSheet;
        return (index / PerSheet, onSheet % Columns * Width, onSheet / Columns * Height);
    }

    /// <summary>
    /// Writes a WebVTT thumbnails track: each interval points at its tile with a #xywh fragment, which web players
    /// read.
    /// </summary>
    public string WebVtt(Func<int, string> sheetUrl)
    {
        var builder = new StringBuilder("WEBVTT\n");
        for (var index = 0; index < Count; index++)
        {
            var (sheet, x, y) = Position(index);
            var start = Time((long)index * IntervalMs);
            var end = Time((long)(index + 1) * IntervalMs);
            var url = sheetUrl(sheet);
            var tile = string.Join(',', new[] { x, y, Width, Height }.Select(value => value.ToString(CultureInfo.InvariantCulture)));
            builder.Append('\n')
                .Append(start).Append(" --> ").Append(end).Append('\n')
                .Append(url).Append("#xywh=").Append(tile)
                .Append('\n');
        }

        return builder.ToString();
    }

    private static string Time(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }
}
