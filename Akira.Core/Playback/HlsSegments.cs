namespace Akira.Core.Playback;

/// <summary>
/// Represents where the segments of an HLS stream start, in seconds from the start of the file. Encoded video is cut every
/// segment length, copied video only on its own keyframes.
/// </summary>
public sealed class HlsSegments
{
    // A keyframe this close to the end would make a last segment the player has to ask for for almost nothing
    private const double ShortestTail = 1;

    private readonly double[] _starts;
    private readonly double[] _keyframes;

    private HlsSegments(double[] starts, double duration, bool onKeyframes, double[] keyframes)
    {
        _starts = starts;
        Duration = duration;
        OnKeyframes = onKeyframes;
        _keyframes = keyframes;
    }

    public double Duration { get; }
    public bool OnKeyframes { get; }
    public int Count => _starts.Length;

    public double Start(int index) => _starts[index];

    public double End(int index) => index + 1 < _starts.Length ? _starts[index + 1] : Duration;

    // Where the video of a segment really begins: the playlist starts the first one at 0, its video starts on the first keyframe
    public double VideoStart(int index) => index == 0 && _keyframes.Length > 0 ? _keyframes[0] : _starts[index];

    /// <summary>
    /// Returns the first keyframe after where the video of a segment begins, or the end.
    /// </summary>
    public double KeyframeAfter(int index)
    {
        var start = VideoStart(index);
        var found = Array.BinarySearch(_keyframes, start);
        var next = found >= 0 ? found + 1 : ~found;
        return next < _keyframes.Length ? _keyframes[next] : Duration;
    }

    public double Longest => Enumerable.Range(0, Count).Max(index => End(index) - Start(index));

    /// <summary>
    /// Lays out segments of one length, the last one shorter.
    /// </summary>
    public static HlsSegments Uniform(long durationMs, TimeSpan length)
    {
        var count = Math.Max(1, (int)Math.Ceiling(durationMs / length.TotalMilliseconds));
        var starts = Enumerable.Range(0, count).Select(index => index * length.TotalSeconds).ToArray();
        return new HlsSegments(starts, durationMs / 1000.0, false, []);
    }

    /// <summary>
    /// Lays out segments that each start on a keyframe and last at least length, so copied video is cut the same way
    /// whichever run of ffmpeg writes a segment. keyframes are seconds from the start of the file.
    /// </summary>
    public static HlsSegments FromKeyframes(IEnumerable<double> keyframes, long durationMs, TimeSpan length)
    {
        var duration = durationMs / 1000.0;
        var ordered = keyframes.Select(keyframe => Math.Clamp(keyframe, 0, duration)).Distinct().Order().ToArray();
        var starts = new List<double> { 0 };
        foreach (var keyframe in ordered)
        {
            if (keyframe > duration - ShortestTail)
            {
                break;
            }

            if (keyframe - starts[^1] >= length.TotalSeconds)
            {
                starts.Add(keyframe);
            }
        }

        return new HlsSegments([.. starts], duration, true, ordered);
    }
}
