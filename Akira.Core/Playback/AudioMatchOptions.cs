namespace Akira.Core.Playback;

/// <summary>
/// Represents the limits of an audio fingerprint match. A shared part counts when it is from MinLength to MaxLength
/// long.
/// </summary>
public sealed record AudioMatchOptions(
    TimeSpan MinLength,
    TimeSpan MaxLength,
    // Two fingerprint points this close are the same sound; chromaprint points are 32 bit
    int MaxBitErrors = 6,
    // Silence or a cut inside a shared part does not split it
    double MaxGapSeconds = 3.5,
    int CandidateShifts = 12);
