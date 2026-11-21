using System.Globalization;

namespace Akira.Infrastructure.Transcoding;

/// <summary>
/// Reads ffmpeg's -progress output (key=value lines, each report closed by a progress line) of one run.
/// </summary>
public sealed class FfmpegProgress
{
    private readonly Dictionary<string, string> _values = new();
    private (DateTimeOffset At, double OutTimeMs, double Frame)? _previous;

    /// <summary>
    /// Takes one line and returns a sample when it closes a report that follows an earlier one.
    /// </summary>
    public TranscodeSample? Read(string line, DateTimeOffset now)
    {
        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return null;
        }

        var key = line[..separator].Trim();
        _values[key] = line[(separator + 1)..].Trim();
        if (key != "progress")
        {
            return null;
        }

        var outTime = Number(_values.GetValueOrDefault("out_time_us")) / 1000;
        var frame = Number(_values.GetValueOrDefault("frame")) ?? 0;
        _values.Clear();

        // With -copyts ffmpeg counts its output time from the first output it reported, not from the start of the file
        // or the seek, so it says nothing about where in the file the run is. Measured between two reports it is the
        // real pace. A report before the first output says 0.
        if (outTime is not > 0)
        {
            return null;
        }

        var previous = _previous;
        _previous = (now, outTime.Value, frame);
        if (previous is not { } before || now - before.At < TimeSpan.FromMilliseconds(500) || outTime.Value < before.OutTimeMs)
        {
            return null;
        }

        var seconds = (now - before.At).TotalSeconds;
        var speed = (outTime.Value - before.OutTimeMs) / 1000 / seconds;
        var fps = Math.Max(0, frame - before.Frame) / seconds;
        return new TranscodeSample(now, speed, fps);
    }

    private static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}
