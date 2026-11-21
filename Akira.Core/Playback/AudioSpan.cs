namespace Akira.Core.Playback;

/// <summary>
/// Represents a stretch of one file's audio, in seconds from its start.
/// </summary>
public sealed record AudioSpan(double Start, double End)
{
    public double Length => End - Start;
}
