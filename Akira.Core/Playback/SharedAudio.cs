namespace Akira.Core.Playback;

/// <summary>
/// Represents where two files share the same audio, in seconds from the start of each fingerprint.
/// </summary>
public sealed record SharedAudio(double StartA, double EndA, double StartB, double EndB)
{
    public double Length => EndA - StartA;
}
